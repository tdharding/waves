using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The one folder the level select's river presets live in, and the only place its path is
/// written down.
///
/// A folder rather than "wherever it was saved" because the river's look is authored in two
/// windows — the water in one, the stone in the other — and two windows saving by file panel had
/// already put two presets of the same name in two different folders, each holding half of one
/// world's look, with the world reading whichever it happened to be pointed at. Naming the folder
/// once here is what stops that: every window offers the same list, and a new preset lands where
/// the others are without anyone having to steer a file panel.
///
/// Both kinds of preset live here together — <see cref="LevelSelectRiverWaterPreset"/> and
/// <see cref="LevelSelectRiverStructurePreset"/> — and each picker shows only its own kind, so
/// the folder reads as one world's worth of look while no window can offer the wrong half.
///
/// Presets outside the folder still work — they are referenced by guid like any asset, and the
/// object fields in the tuners and the designer still take one. The picker just says so rather
/// than quietly dropping it off the end of the list.
/// </summary>
public static class LevelSelectRiverPresetLibrary
{
    /// <summary>Where presets are kept. Alongside the world data that references them.</summary>
    public const string Folder = "Assets/ScriptsData/DataScripts/LevelSelectRiverPresets";

    /// <summary>Shown in a picker for "no preset", which is a real choice: a world with none
    /// wears no ripples and no run shading at all.</summary>
    private const string NoneLabel = "None";

    /// <summary>
    /// Every preset in the folder, by name. Read fresh each time rather than cached — presets
    /// are made and renamed while these windows are open, and a stale list is worse than a
    /// slightly slow one at the rate a picker is drawn.
    /// </summary>
    public static List<T> All<T>() where T : ScriptableObject
    {
        if (!AssetDatabase.IsValidFolder(Folder)) return new List<T>();

        return AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { Folder })
                            .Select(AssetDatabase.GUIDToAssetPath)
                            .Select(AssetDatabase.LoadAssetAtPath<T>)
                            .Where(p => p != null)
                            .OrderBy(p => p.name)
                            .ToList();
    }

    /// <summary>
    /// Draws the folder's presets as a dropdown and returns whichever is chosen — the one that
    /// came in if nothing was.
    ///
    /// A preset held from outside the folder is added to the end of the list and labelled with
    /// where it is, so pointing at a stray one is visible rather than silent. That is exactly the
    /// case this whole folder exists to make obvious.
    /// </summary>
    public static T DrawPicker<T>(GUIContent label, T current) where T : ScriptableObject
    {
        var presets = All<T>();

        bool strayHeld = current != null && !presets.Contains(current);
        if (strayHeld) presets.Add(current);

        var labels = new List<string> { NoneLabel };
        labels.AddRange(presets.Select(
            p => p == current && strayHeld
                ? $"{p.name}   (outside {System.IO.Path.GetFileName(Folder)})"
                : p.name));

        int index = current == null ? 0 : presets.IndexOf(current) + 1;

        int chosen = EditorGUILayout.Popup(label, index, labels.ToArray());

        return chosen <= 0 ? null : presets[chosen - 1];
    }

    /// <summary>
    /// The folder, made if it is not there yet, so a Save as New never opens a file panel on a
    /// path that does not exist — Unity silently falls back to Assets/ when it does, which is how
    /// presets end up scattered in the first place.
    /// </summary>
    public static string EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(Folder)) return Folder;

        string[] parts = Folder.Split('/');
        string built = parts[0];                       // "Assets"

        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{built}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
            built = next;
        }

        return Folder;
    }
}
