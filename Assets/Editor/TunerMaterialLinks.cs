using UnityEditor;
using UnityEngine;

/// <summary>
/// The Materials foldout at the top of the level select tuners: each material the tuner shades,
/// and the shader graph behind it, for getting to either in one click.
///
/// Read off the world rather than held by the tuner — the designer data (or the scene's landscape)
/// says which material is really on the stone, so this follows it when that is swapped. A click
/// on either field pings the asset; Open opens the graph.
/// </summary>
public static class TunerMaterialLinks
{
    private static LevelSelectDataController _controller;
    private static LandscapeTool             _landscape;

    /// <summary>Draws the foldout. Labels and materials are paired by index; a null is "not set".</summary>
    public static void Draw(string prefKey, string[] labels, Material[] materials)
    {
        bool open = EditorGUILayout.Foldout(EditorPrefs.GetBool(prefKey, true),
                                            "Materials", true, EditorStyles.foldoutHeader);
        EditorPrefs.SetBool(prefKey, open);
        if (!open) return;

        for (int i = 0; i < labels.Length; i++)
            DrawRow(labels[i], materials[i]);
    }

    private static void DrawRow(string label, Material material)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            // Not disabled, so a click still pings — what is picked is simply not kept.
            EditorGUILayout.ObjectField(label, material, typeof(Material), false);

            var graph = GraphOf(material);
            EditorGUILayout.ObjectField(graph, typeof(Object), false, GUILayout.Width(150f));

            using (new EditorGUI.DisabledScope(graph == null))
                if (GUILayout.Button("Open", GUILayout.Width(48f)))
                    AssetDatabase.OpenAsset(graph);
        }
    }

    /// <summary>The asset the material's shader was imported from — the .shadergraph for a graph.</summary>
    private static Object GraphOf(Material material)
    {
        if (material == null || material.shader == null) return null;

        string path = AssetDatabase.GetAssetPath(material.shader);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
    }

    /// <summary>
    /// The world's designer data: the Level Select Designer's workspace while it is open,
    /// otherwise the scene's.
    /// </summary>
    public static LevelSelectDesignerData WorldData()
    {
        if (LevelSelectAestheticsPump.Preview != null) return LevelSelectAestheticsPump.Preview;

        if (_controller == null)
            _controller = Object.FindFirstObjectByType<LevelSelectDataController>();

        return _controller != null ? _controller.DesignerData : null;
    }

    /// <summary>The material on the scene's landscape tiles.</summary>
    public static Material LandscapeMaterial()
    {
        if (_landscape == null)
            _landscape = Object.FindFirstObjectByType<LandscapeTool>();

        return _landscape != null ? _landscape.targetMaterial : null;
    }
}
