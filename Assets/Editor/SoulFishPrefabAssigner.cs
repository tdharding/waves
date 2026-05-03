using UnityEditor;
using UnityEngine;

/// <summary>
/// Bulk-assigns a fish prefab to all SoulData assets in Resources/Souls/.
/// Tools → Soul System → Assign Fish Prefab to All Souls
/// </summary>
public static class SoulFishPrefabAssigner
{
    private const string DefaultFishPrefabPath = "Assets/Prefab/SoulFish/SoulFish.prefab";
    private const string SoulsFolder           = "Assets/Resources/Souls";

    [MenuItem("Tools/Soul System/Assign Fish Prefab to All Souls")]
    public static void AssignFishPrefabs()
    {
        // Load the default fish prefab
        var defaultFish = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultFishPrefabPath);
        if (defaultFish == null)
        {
            EditorUtility.DisplayDialog(
                "Fish Prefab Not Found",
                $"Could not find a fish prefab at:\n{DefaultFishPrefabPath}\n\nCheck the path and try again.",
                "OK");
            return;
        }

        // Find all SoulData assets
        string[] guids = AssetDatabase.FindAssets("t:SoulData", new[] { SoulsFolder });
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("No Souls Found",
                $"No SoulData assets found in {SoulsFolder}.", "OK");
            return;
        }

        // Count how many already have a prefab set
        int alreadySet = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var soul = AssetDatabase.LoadAssetAtPath<SoulData>(path);
            if (soul != null && soul.fishPrefab != null) alreadySet++;
        }

        bool overwriteAll = false;
        if (alreadySet > 0)
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Assign Fish Prefab",
                $"{guids.Length} SoulData assets found.\n" +
                $"{alreadySet} already have a fish prefab assigned.\n\n" +
                "Assign to empty slots only, or overwrite all?",
                "Empty Only", "Cancel", "Overwrite All");

            if (choice == 1) return;        // Cancel
            if (choice == 2) overwriteAll = true; // Overwrite All
        }

        // Assign
        int assigned = 0;
        int skipped  = 0;

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var soul = AssetDatabase.LoadAssetAtPath<SoulData>(path);
                if (soul == null) continue;

                EditorUtility.DisplayProgressBar(
                    "Assigning Fish Prefabs",
                    $"{soul.soulName} ({i + 1}/{guids.Length})",
                    (float)(i + 1) / guids.Length);

                if (soul.fishPrefab != null && !overwriteAll)
                {
                    skipped++;
                    continue;
                }

                soul.fishPrefab = defaultFish;
                EditorUtility.SetDirty(soul);
                assigned++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"[SoulFishPrefabAssigner] Done — {assigned} assigned, {skipped} skipped (already set).");
        EditorUtility.DisplayDialog(
            "Fish Prefab Assigned",
            $"Assigned to {assigned} soul(s).\n" +
            (skipped > 0 ? $"{skipped} skipped (already had a prefab)." : ""),
            "OK");
    }
}
