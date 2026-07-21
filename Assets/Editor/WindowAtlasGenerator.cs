using System.IO;
using UnityEditor;
using UnityEngine;

// Generates the placeholder window-glyph atlas consumed by WindowTiling.hlsl and
// applies the import settings the shader needs (Point, uncompressed, no mips,
// linear, Clamp, NPOT none). Re-running overwrites the PNG in place.
//
// Layout contract (must match WindowTiling.hlsl):
//   8 variant columns × 9 role rows, 9 px cells → 72×81 px.
//   Row order, top of the PNG downward ("facade order"):
//     0 TL corner · 1 top edge · 2 TR corner · 3 left edge · 4 interior
//     5 right edge · 6 BL corner · 7 bottom/waterline row · 8 BR corner
//   White = window glyph, transparent = bare rock. Blank tiles are legal — the
//   shader shows rock there, which reads as natural density variation.
//
// The artist replacement can use ANY cell resolution (e.g. 64 px cells for brushy
// glyphs) as long as the 8×9 grid layout is kept; switch the material's Sampler
// State to Linear for soft-edged art.
public static class WindowAtlasGenerator
{
    const int Cell = 9;   // px per tile in the placeholder
    const int Cols = 8;   // variant columns — matches _WindowAtlasGrid.x
    const int Rows = 9;   // role rows      — matches _WindowAtlasGrid.y
    const string OutputPath = "Assets/TextureMatShader/Maze/WindowAtlas.png";

    // 5×5 glyph library, strings top → bottom, '#' = white.
    static readonly string[] Plus = { "..#..", "..#..", "#####", "..#..", "..#.." };
    static readonly string[] Tee  = { "#####", "..#..", "..#..", "..#..", "..#.." };
    static readonly string[] Eye  = { "#####", "..#..", "..#..", "..#..", "#####" };
    static readonly string[] Ell  = { "#....", "#....", "#....", "#....", "#####" };
    static readonly string[] Aich = { "#...#", "#...#", "#####", "#...#", "#...#" };
    static readonly string[] Bars = { "#####", ".....", "#####", ".....", "#####" };
    static readonly string[] Ring = { "#####", "#...#", "#...#", "#...#", "#####" };
    static readonly string[] Post = { "..#..", "..#..", "..#..", "..#..", "..#.." };

    static readonly string[][] InteriorGlyphs = { Plus, Tee, Eye, Ell, Aich, Ring };
    static readonly string[][] TopGlyphs      = { Tee, Plus, Eye, Post, Bars, Aich };
    static readonly string[][] SideGlyphs     = { Post, Eye, Ell, Tee, Bars, Ring };

    [MenuItem("Tools/Waves/Generate Window Atlas")]
    public static void Generate()
    {
        int w = Cols * Cell, h = Rows * Cell;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        tex.SetPixels32(new Color32[w * h]); // clear to transparent

        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                bool[,] tile = BuildTile(r, c);
                if (tile != null) Blit(tex, tile, c, r);
            }

        tex.Apply();
        File.WriteAllBytes(OutputPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(OutputPath);
        var importer = AssetImporter.GetAtPath(OutputPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[WindowAtlasGenerator] Could not get TextureImporter for {OutputPath} — set import settings manually (Point, uncompressed, no mips, sRGB off, Clamp, NPOT None).");
            return;
        }
        importer.textureType        = TextureImporterType.Default;
        importer.sRGBTexture        = false;
        importer.mipmapEnabled      = false;
        importer.filterMode         = FilterMode.Point;
        importer.wrapMode           = TextureWrapMode.Clamp;
        importer.npotScale          = TextureImporterNPOTScale.None; // 72×81 is NPOT — required
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable         = false;
        importer.SaveAndReimport();

        Debug.Log($"[WindowAtlasGenerator] Wrote {OutputPath} ({w}×{h}: {Cols} variants × {Rows} roles).");
    }

    // Builds one tile as a [x, y] canvas (y = 0 at the tile's top), or null for blank.
    static bool[,] BuildTile(int pngRow, int variant)
    {
        var t = new bool[Cell, Cell];
        switch (pngRow)
        {
            case 4: // interior — plain glyph, last two columns blank
                if (variant >= InteriorGlyphs.Length) return null;
                Draw(t, InteriorGlyphs[variant], 2, 2);
                return t;

            case 1: // top edge — lintel line above the glyph
                if (variant >= TopGlyphs.Length) return null;
                HLine(t, 1, 1, 7);
                Draw(t, TopGlyphs[variant], 2, 3);
                return t;

            case 3: // left edge — trim line on the block-edge side, glyph pushed right
                if (variant >= SideGlyphs.Length) return null;
                VLine(t, 1, 1, 7);
                Draw(t, SideGlyphs[variant], 3, 2);
                return t;

            case 5: // right edge — mirror of left
                if (variant >= SideGlyphs.Length) return null;
                VLine(t, 7, 1, 7);
                Draw(t, SideGlyphs[variant], 1, 2);
                return t;

            case 7: // bottom / waterline row — ground-floor shapes touching the tile bottom
                switch (variant)
                {
                    case 0: Rect(t, 3, 4, 5, 8); return t;                       // door
                    case 1: Rect(t, 3, 5, 5, 8); HLine(t, 4, 2, 6); return t;    // capped arch
                    case 2: Rect(t, 2, 5, 6, 8); return t;                       // wide gate
                    case 3: VLine(t, 4, 2, 8); HLine(t, 8, 1, 7); return t;      // T standing on its base
                    case 4: Draw(t, Eye, 2, 0); Rect(t, 3, 6, 5, 8); return t;   // window over a doorstep
                    default: return null;
                }

            case 0: // TL corner — accents run toward the top and left face edges
                HLine(t, 1, 1, 8); VLine(t, 1, 1, 8);
                return CornerMark(t, variant, 4);

            case 2: // TR corner
                HLine(t, 1, 0, 7); VLine(t, 7, 1, 8);
                return CornerMark(t, variant, 2);

            case 6: // BL corner — vertical accent plus a baseline touching the waterline
                VLine(t, 1, 0, 8); HLine(t, 8, 1, 8);
                return CornerMark(t, variant, 4);

            case 8: // BR corner
                VLine(t, 7, 0, 8); HLine(t, 8, 0, 7);
                return CornerMark(t, variant, 2);

            default:
                return null;
        }
    }

    // Small interior mark for corner tiles; x0 shifts it away from the accent lines.
    static bool[,] CornerMark(bool[,] t, int variant, int x0)
    {
        switch (variant)
        {
            case 0: Rect(t, x0, 4, x0 + 2, 6); return t;                  // small square
            case 1: VLine(t, x0 + 1, 3, 6); return t;                     // short post
            case 2: HLine(t, 5, x0, x0 + 3); return t;                    // short bar
            case 3: HLine(t, 5, x0, x0 + 2); VLine(t, x0 + 1, 4, 6); return t; // small plus
            default: return null;                                          // blanks
        }
    }

    static void Draw(bool[,] t, string[] glyph, int ox, int oy)
    {
        for (int y = 0; y < glyph.Length; y++)
            for (int x = 0; x < glyph[y].Length; x++)
                if (glyph[y][x] == '#') Set(t, ox + x, oy + y);
    }

    static void HLine(bool[,] t, int y, int x0, int x1) { for (int x = x0; x <= x1; x++) Set(t, x, y); }
    static void VLine(bool[,] t, int x, int y0, int y1) { for (int y = y0; y <= y1; y++) Set(t, x, y); }
    static void Rect(bool[,] t, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Set(t, x, y);
    }

    static void Set(bool[,] t, int x, int y)
    {
        if (x >= 0 && x < Cell && y >= 0 && y < Cell) t[x, y] = true;
    }

    // Writes a tile into the texture. Tile space is y-down from the tile top;
    // SetPixel space is y-up from the image bottom, hence the flip — this is what
    // makes the PNG read in facade order (row 0 at the top) in image editors.
    static void Blit(Texture2D tex, bool[,] tile, int col, int pngRow)
    {
        for (int py = 0; py < Cell; py++)
            for (int px = 0; px < Cell; px++)
                if (tile[px, py])
                    tex.SetPixel(col * Cell + px, Rows * Cell - 1 - (pngRow * Cell + py), Color.white);
    }
}
