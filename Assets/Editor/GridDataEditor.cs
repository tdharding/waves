using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GridData))]
public class GridDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        float[] offsets = GetTierOffsets();

        SerializedProperty prop = serializedObject.GetIterator();
        prop.NextVisible(true); // skip m_Script

        while (prop.NextVisible(false))
        {
            if (prop.name == "tiers")
            {
                DrawTiersWithFloorLabels(prop, offsets);
            }
            else
            {
                EditorGUILayout.PropertyField(prop, true);
            }
        }

        EditorGUILayout.Space(4);
        DrawTierSummary(target as GridData, offsets);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawTiersWithFloorLabels(SerializedProperty tiersProp, float[] offsets)
    {
        EditorGUILayout.LabelField("Tiers", EditorStyles.boldLabel);

        for (int i = 0; i < tiersProp.arraySize; i++)
        {
            SerializedProperty tier     = tiersProp.GetArrayElementAtIndex(i);
            SerializedProperty nameProp = tier.FindPropertyRelative("name");
            SerializedProperty slotProp = tier.FindPropertyRelative("yOffsetSlot");

            string floorLabel = offsets != null
                ? WaterLevelModifier.FloorLabel(slotProp.intValue, offsets)
                : $"slot {slotProp.intValue}";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Tier {i} — {floorLabel}", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(tier, new GUIContent(nameProp.stringValue), true);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (GUILayout.Button("Add Tier"))
        {
            tiersProp.arraySize++;
            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Shows a compact tier floor summary — also used by other editors referencing GridData.
    /// </summary>
    internal static void DrawTierSummary(GridData gridData, float[] offsets = null)
    {
        if (gridData == null || gridData.tiers == null || gridData.tiers.Count == 0)
            return;

        if (offsets == null) offsets = GetTierOffsets();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Tier Floors", EditorStyles.miniBoldLabel);

        foreach (var tier in gridData.tiers)
        {
            string floorLabel = offsets != null
                ? WaterLevelModifier.FloorLabel(tier.yOffsetSlot, offsets)
                : $"slot {tier.yOffsetSlot}";

            float y = (offsets != null && tier.yOffsetSlot < offsets.Length)
                ? offsets[tier.yOffsetSlot]
                : tier.yOffset;

            EditorGUILayout.LabelField($"  {tier.name}", $"{floorLabel}  (Y = {y:+0.##;-0.##;0})",
                EditorStyles.miniLabel);
        }

        EditorGUILayout.EndVertical();
    }

    internal static float[] GetTierOffsets()
    {
        string[] guids = AssetDatabase.FindAssets("t:TierConfig");
        if (guids.Length == 0) return null;
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        var config = AssetDatabase.LoadAssetAtPath<TierConfig>(path);
        return config?.offsets;
    }
}
