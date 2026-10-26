using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A dropdown of the fog maps saved in the FogMaps folder, in place of an object field and its
/// file-type search. Every saved map is one click away, by name.
///
/// A map assigned from somewhere outside the folder is still listed, so picking from the dropdown
/// never silently drops a reference that is already there.
/// </summary>
public static class FogMapPicker
{
    public const string Folder = "Assets/Resources/FogMaps";

    static List<FogMap> _maps;
    static List<string> _names;

    // The list is cached because the Fog Map window repaints every frame while the wind runs;
    // it is thrown away whenever the project changes, so a newly saved map appears straight away.
    [InitializeOnLoadMethod]
    static void Hook() => EditorApplication.projectChanged += () => _maps = null;

    static void Build()
    {
        _maps  = new List<FogMap>();
        _names = new List<string>();
        if (!AssetDatabase.IsValidFolder(Folder)) return;

        foreach (string guid in AssetDatabase.FindAssets("t:FogMap", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var map = AssetDatabase.LoadAssetAtPath<FogMap>(path);
            if (map == null) continue;
            // Relative to the folder, so a map in a subfolder shows as a submenu.
            string rel = path.Substring(Folder.Length + 1);
            _maps.Add(map);
            _names.Add(rel.EndsWith(".asset") ? rel.Substring(0, rel.Length - 6) : rel);
        }
    }

    /// <summary>Labelled dropdown row, for inspector-style sections.</summary>
    public static FogMap Popup(string label, FogMap current)
    {
        Options(current, out var maps, out var names, out int index);
        int picked = EditorGUILayout.Popup(new GUIContent(label), index, names);
        return picked == index ? current : maps[picked];
    }

    /// <summary>Unlabelled dropdown in toolbar style.</summary>
    public static FogMap ToolbarPopup(FogMap current, params GUILayoutOption[] options)
    {
        Options(current, out var maps, out var names, out int index);
        int picked = EditorGUILayout.Popup(index, names, EditorStyles.toolbarPopup, options);
        return picked == index ? current : maps[picked];
    }

    static void Options(FogMap current, out List<FogMap> maps, out GUIContent[] names, out int index)
    {
        if (_maps == null) Build();

        maps = new List<FogMap>(_maps.Count + 2) { null };
        var labels = new List<GUIContent>(_maps.Count + 2) { new GUIContent("None") };
        for (int i = 0; i < _maps.Count; i++)
        {
            maps.Add(_maps[i]);
            labels.Add(new GUIContent(_names[i]));
        }

        index = maps.IndexOf(current);
        if (index < 0)
        {
            maps.Add(current);
            labels.Add(new GUIContent($"{current.name} (outside FogMaps)"));
            index = maps.Count - 1;
        }
        names = labels.ToArray();
    }
}
