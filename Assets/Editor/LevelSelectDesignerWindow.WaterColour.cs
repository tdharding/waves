using UnityEditor;
using UnityEngine;

/// <summary>
/// Water Colour Override — a path's water in its own colour instead of the Level Select River
/// Tuner's Water Colour. Stamped onto each of the path's runs, which put it on their water as a
/// property block over the tuner's global, so it is live: no rebuild.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    /// <summary>The override toggle and its colour, in Path Properties under Run Type.</summary>
    private void DrawPathWaterColour(LevelSelectDesignerData.DesignerPath path)
    {
        bool  on     = path.waterColourOverride;
        Color colour = path.waterColour;

        EditorGUILayout.BeginHorizontal();
        on = EditorGUILayout.Toggle(
            new GUIContent("Water Colour Override",
                "Gives this path's water its own colour in place of the Level Select River " +
                "Tuner's Water Colour. The lines, grain and depth still go on over it."),
            on);
        using (new EditorGUI.DisabledScope(!on))
            colour = EditorGUILayout.ColorField(GUIContent.none, colour);
        EditorGUILayout.EndHorizontal();

        if (on == path.waterColourOverride && colour == path.waterColour) return;

        Undo.RecordObject(_data, "Edit Water Colour Override");
        path.waterColourOverride = on;
        path.waterColour         = colour;
        EditorUtility.SetDirty(_data);

        foreach (var record in FindObjectsOfType<RiverRunMesh>())
            if (PathForRunOrRiver(record) == path) StampWaterColour(record, undoable: true);
        foreach (var record in FindObjectsOfType<RiverPoolMesh>())
            if (PathForPool(record) == path) StampPoolWaterColour(record, undoable: true);
    }

    // The path a pool takes its shape from decides its colour too.
    private LevelSelectDesignerData.DesignerPath PathForPool(RiverPoolMesh record)
    {
        var pool = PoolForRecord(record);
        return pool != null ? PoolPath(pool) : PathForRiver(record.riverName);
    }

    /// <summary>Copies that path's override onto a pool and puts it on the pool's water.</summary>
    private void StampPoolWaterColour(RiverPoolMesh record, bool undoable = false)
    {
        var path = PathForPool(record);
        bool  on     = path != null && path.waterColourOverride;
        Color colour = path != null ? path.waterColour : record.waterColour;

        if (on != record.waterColourOverride || colour != record.waterColour)
        {
            if (undoable) Undo.RecordObject(record, "Edit Water Colour Override");
            record.waterColourOverride = on;
            record.waterColour         = colour;
            EditorUtility.SetDirty(record);
        }

        record.ApplyWaterColour();
    }

    /// <summary>Copies the path's override onto one of its runs and puts it on the water.</summary>
    private void StampWaterColour(RiverRunMesh record, bool undoable = false)
    {
        var path = PathForRunOrRiver(record);
        bool  on     = path != null && path.waterColourOverride;
        Color colour = path != null ? path.waterColour : record.waterColour;

        if (on != record.waterColourOverride || colour != record.waterColour)
        {
            if (undoable) Undo.RecordObject(record, "Edit Water Colour Override");
            record.waterColourOverride = on;
            record.waterColour         = colour;
            EditorUtility.SetDirty(record);
        }

        record.ApplyWaterColour();
    }
}
