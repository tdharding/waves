using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Tools > Save Current Layout: writes the window arrangement as it is now over the layout last
// picked from the Layout dropdown, with no name prompt. Unity keeps all of this on its internal
// WindowLayout class, so it is reached by reflection.
public static class SaveCurrentLayoutMenu
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("Tools/Save Current Layout (Overwrite)", false, 1)]
    public static void SaveCurrentLayout()
    {
        Type windowLayout = typeof(Editor).Assembly.GetType("UnityEditor.WindowLayout");
        if (windowLayout == null)
        {
            Debug.LogError("[Save Current Layout] UnityEditor.WindowLayout not found in this Unity version.");
            return;
        }

        string name = windowLayout.GetProperty("lastLoadedLayoutName", Flags)?.GetValue(null) as string;
        string path = FindLayoutFile(windowLayout, name);
        if (path == null)
        {
            // No saved layout to overwrite yet, so fall back to Unity's own prompt.
            Debug.LogWarning($"[Save Current Layout] No saved layout file for '{name}' — opening Save Layout instead.");
            EditorApplication.ExecuteMenuItem("Window/Layouts/Save Layout...");
            return;
        }

        MethodInfo save = windowLayout.GetMethod("SaveWindowLayout", Flags, null,
                                                 new[] { typeof(string) }, null);
        if (save == null)
        {
            Debug.LogError("[Save Current Layout] WindowLayout.SaveWindowLayout(string) not found in this Unity version.");
            return;
        }

        save.Invoke(null, new object[] { path });
        Debug.Log($"[Save Current Layout] Saved over '{name}' ({path}).");
    }

    // Saved layouts live in the mode folder (Layouts/default); older versions used the folder above it.
    static string FindLayoutFile(Type windowLayout, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (string folderProperty in new[] { "layoutsModePreferencesPath", "layoutsPreferencesPath" })
        {
            if (!(windowLayout.GetProperty(folderProperty, Flags)?.GetValue(null) is string folder)) continue;
            string path = Path.Combine(folder, name + ".wlt");
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
