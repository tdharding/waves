using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The folder the landscape's hand-drawn decals live in and the sheet they are gathered onto —
/// the landscape's copy of <see cref="RiverRunDecalLibrary"/>, whose sheet building it shares so
/// the two can never fit a drawing differently. Drop a PNG in the folder, press Rebuild Sheet in
/// the Level Select Landscape Tuner, tick it into a decal group, and Save.
///
/// A subfolder of LandscapeMat rather than LandscapeMat itself, because every PNG or JPG directly
/// in the folder is gathered as a drawing — the landscape textures already in LandscapeMat would
/// land on the sheet.
/// </summary>
public static class LandscapeDecalLibrary
{
    public const string Folder      = "Assets/TextureMatShader/LandscapeMat/LandscapeDecals";
    public const string SheetFolder = Folder + "/Generated";
    public const string SheetPath   = SheetFolder + "/LandscapeDecalSheet.asset";

    /// <summary>
    /// Where the normal maps for the normal map decal groups are kept — a subfolder, so the colour
    /// sheet never gathers them — and where their own sheet goes.
    /// </summary>
    public const string NormalFolder    = Folder + "/NormalMaps";
    public const string NormalSheetPath = NormalFolder + "/Generated/LandscapeNormalDecalSheet.asset";

    /// <summary>The normal maps in their folder, in name order — the order they land on their sheet.</summary>
    public static List<Texture2D> AllNormals() => RiverRunDecalLibrary.All(NormalFolder);

    /// <summary>The drawings in the folder, in name order — the order they land on the sheet.</summary>
    public static List<Texture2D> All() => RiverRunDecalLibrary.All(Folder);

    /// <summary>Why the sheet no longer matches the folder, in a sentence — or null when it does.</summary>
    public static string Behind(LandscapeShadingSettings settings)
    {
        if (settings == null) return null;

        return RiverRunDecalLibrary.Behind(Folder, SheetPath, settings.decalSheet,
                                           settings.decalSheetHeld, settings.decalSheetDrawings);
    }

    /// <summary>
    /// Gathers the folder onto the sheet and writes what it laid out into the settings. An empty
    /// folder leaves them holding no sheet, which is what turns the decals off.
    /// </summary>
    public static bool Build(LandscapeShadingSettings into, out string message)
    {
        if (into == null) { message = "No settings to build into."; return false; }

        bool ok = RiverRunDecalLibrary.BuildSheet(Folder, SheetPath,
                                                  out RiverRunDecalLibrary.BuiltSheet built,
                                                  out message);

        // Held -1: the sheet could not be written, so the settings keep what they had.
        if (!ok && built.Held < 0) return false;

        into.decalSheet           = built.Sheet;
        into.decalSheetColumns    = built.Columns;
        into.decalSheetRows       = built.Rows;
        into.decalSheetHeld       = built.Held;
        into.decalSheetCellPixels = built.CellPixels;
        into.decalSheetDrawings   = built.Drawings;
        return ok;
    }

    /// <summary>The same for the normal maps and their sheet.</summary>
    public static string BehindNormals(LandscapeShadingSettings settings)
    {
        if (settings == null) return null;

        return RiverRunDecalLibrary.Behind(NormalFolder, NormalSheetPath, settings.normalDecalSheet,
                                           settings.normalDecalSheetHeld, settings.normalDecalSheetDrawings);
    }

    /// <summary>
    /// The same for the normal maps, onto a LINEAR sheet: the shader reads each texel straight
    /// back as a direction, which an sRGB sheet would bend on the way.
    /// </summary>
    public static bool BuildNormals(LandscapeShadingSettings into, out string message)
    {
        if (into == null) { message = "No settings to build into."; return false; }

        bool ok = RiverRunDecalLibrary.BuildSheet(NormalFolder, NormalSheetPath,
                                                  out RiverRunDecalLibrary.BuiltSheet built,
                                                  out message, linear: true);
        if (!ok && built.Held < 0) return false;

        into.normalDecalSheet           = built.Sheet;
        into.normalDecalSheetColumns    = built.Columns;
        into.normalDecalSheetRows       = built.Rows;
        into.normalDecalSheetHeld       = built.Held;
        into.normalDecalSheetCellPixels = built.CellPixels;
        into.normalDecalSheetDrawings   = built.Drawings;
        return ok;
    }
}
