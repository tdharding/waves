using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The one folder the arenas' numeral drawings live in, and the material each one is shown on.
///
/// A folder rather than a list of object fields, for the same reason the river decals have one
/// (<see cref="RiverRunDecalLibrary"/>): the numerals are drawings that arrive one at a time, and
/// a folder picks a new one up the moment it is dropped in. Which arena a drawing belongs to is
/// read off its FILE NAME — the first run of digits in it, so "Door1Image.png" is arena 1 — which
/// means the drawings can be named however reads best as long as the number is somewhere in there.
///
/// ── Why a material per numeral, and not one sheet ─────────────────────────────
/// The river decals are gathered onto one sheet because their shader picks a cell per PIXEL, so
/// every drawing has to be bound at once. Nothing of the sort happens here: a door shows one
/// numeral, chosen once, when the level select is generated. So each numeral gets its own
/// material — a VARIANT of the one hand-wired on the designer data, overriding nothing but the
/// texture. Tune the parent and every numeral follows it; the variants hold no settings of their
/// own to drift out of step.
///
/// The variants are generated, like the meshes are: drop a drawing in the folder, generate, and
/// its material is written. Nothing has to be made or wired by hand for a numeral to appear.
/// </summary>
public static class ArenaNumeralLibrary
{
    /// <summary>Where the numeral drawings are kept.</summary>
    public const string Folder = "Assets/TextureMatShader/NumeralDecals";

    /// <summary>
    /// Where the generated materials go. A subfolder, so scanning the folder above it for
    /// drawings can never pick one of them up.
    /// </summary>
    public const string GeneratedFolder = Folder + "/Generated";

    // The texture slot the drawing is put in. URP's shaders and Shader Graph's own lit and unlit
    // targets all call it _BaseMap; _MainTex is there for a material built on an older shader.
    private static readonly string[] TextureSlots = { "_BaseMap", "_MainTex" };

    /// <summary>
    /// The drawing for one arena number, or null when nobody has drawn it yet — which is the
    /// normal state of most of them, and leaves that arena's disc blank rather than failing.
    /// </summary>
    public static Texture2D DrawingFor(int number)
    {
        if (number <= 0 || !AssetDatabase.IsValidFolder(Folder)) return null;

        foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder })
                                             .Select(AssetDatabase.GUIDToAssetPath)
                                             .OrderBy(p => p))
        {
            // Only the folder itself, never the generated subfolder beneath it.
            if (Path.GetDirectoryName(path)?.Replace('\\', '/') != Folder) continue;
            if (NumberIn(Path.GetFileNameWithoutExtension(path)) != number) continue;

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        return null;
    }

    /// <summary>
    /// The number a drawing's file name claims — the first run of digits in it — or 0 when there
    /// is none, which leaves the drawing out of the numbering rather than guessing at it.
    /// </summary>
    public static int NumberIn(string fileName)
    {
        var hit = Regex.Match(fileName ?? string.Empty, @"\d+");
        return hit.Success && int.TryParse(hit.Value, out int n) ? n : 0;
    }

    /// <summary>
    /// The material showing one arena's numeral: a variant of <paramref name="parent"/> carrying
    /// that numeral's drawing, made on the spot the first time it is asked for and refreshed
    /// after that. Null when the numeral has not been drawn, or when no parent material has been
    /// wired on the designer data — either way the arena's disc is simply left blank.
    /// </summary>
    public static Material MaterialFor(int number, Material parent, out Texture2D drawing)
    {
        drawing = DrawingFor(number);
        if (drawing == null || parent == null) return null;

        if (!AssetDatabase.IsValidFolder(GeneratedFolder))
            AssetDatabase.CreateFolder(Folder, "Generated");

        string path = $"{GeneratedFolder}/ArenaNumeral_{number}.mat";
        var    mat  = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (mat == null)
        {
            mat = new Material(parent);
            AssetDatabase.CreateAsset(mat, path);
        }

        // Re-stated every time rather than only on creation, so pointing the designer data at a
        // different parent material moves every numeral already generated over to it.
        if (mat.parent != parent) mat.parent = parent;

        string slot = TextureSlots.FirstOrDefault(mat.HasProperty);
        if (slot == null)
        {
            Debug.LogWarning($"[ArenaNumerals] {parent.name} has no _BaseMap or _MainTex to put " +
                             $"the numeral drawing in, so arena {number}'s disc is blank.");
            return null;
        }

        if (mat.GetTexture(slot) != drawing) mat.SetTexture(slot, drawing);

        EditorUtility.SetDirty(mat);
        return mat;
    }
}
