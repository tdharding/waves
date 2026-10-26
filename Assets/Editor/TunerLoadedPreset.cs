using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps a tuner's Save off a preset whose values never reached the tuner.
///
/// A tuner puts its last preset back in the Active Preset row when the window opens, but not that
/// preset's values — so the row can name a preset while the fields still hold defaults, and Save
/// would write those defaults over it. Each tuner remembers the preset its values actually came
/// from (Load, picking a preset, Save as New); Save is only offered while that is the one in the row.
/// </summary>
public static class TunerLoadedPreset
{
    private static readonly Color NotLoadedTint = new Color(1f, 0.45f, 0.45f);

    /// <summary>True when the tuner's values came from <paramref name="active"/>.</summary>
    public static bool IsLoaded(Object active, Object loaded) => active != null && active == loaded;

    /// <summary>
    /// Tints what follows red while the row's preset is not the one loaded. Put the returned
    /// colour back in GUI.backgroundColor once the row is drawn.
    /// </summary>
    public static Color TintRow(Object active, Object loaded)
    {
        var before = GUI.backgroundColor;
        if (active != null && active != loaded) GUI.backgroundColor = NotLoadedTint;
        return before;
    }

    /// <summary>The red box under the row, while the row's preset is not the one loaded.</summary>
    public static void DrawNotLoaded(Object active, Object loaded, string loadButton = "Load")
    {
        if (active == null || active == loaded) return;

        EditorGUILayout.HelpBox(
            $"Values not loaded from '{active.name}' — the fields may hold defaults. " +
            $"Press {loadButton} before Save.",
            MessageType.Error);
    }
}
