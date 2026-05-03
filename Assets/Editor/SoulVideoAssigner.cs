using UnityEditor;
using UnityEngine;
using UnityEngine.Video;
using System.Collections.Generic;

/// <summary>
/// Randomly distributes video clips across all SoulData assets in Resources/Souls/.
/// Each clip is spread as evenly as possible; assignment order is shuffled.
/// Tools → Soul System → Randomly Assign Videos to All Souls
/// </summary>
public static class SoulVideoAssigner
{
    private const string SoulsFolder  = "Assets/Resources/Souls";
    private const string VideosFolder = "Assets/Resources/Videos";

    [MenuItem("Tools/Soul System/Randomly Assign Videos to All Souls")]
    public static void AssignVideos()
    {
        // Load all video clips from the videos folder
        string[] videoGuids = AssetDatabase.FindAssets("t:VideoClip", new[] { VideosFolder });
        if (videoGuids.Length == 0)
        {
            EditorUtility.DisplayDialog("No Videos Found",
                $"No VideoClip assets found in {VideosFolder}.\n\nCheck the path and try again.", "OK");
            return;
        }

        var clips = new List<VideoClip>();
        foreach (string guid in videoGuids)
        {
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip != null) clips.Add(clip);
        }

        // Load all SoulData assets
        string[] soulGuids = AssetDatabase.FindAssets("t:SoulData", new[] { SoulsFolder });
        if (soulGuids.Length == 0)
        {
            EditorUtility.DisplayDialog("No Souls Found",
                $"No SoulData assets found in {SoulsFolder}.", "OK");
            return;
        }

        int alreadySet = 0;
        foreach (string guid in soulGuids)
        {
            var soul = AssetDatabase.LoadAssetAtPath<SoulData>(AssetDatabase.GUIDToAssetPath(guid));
            if (soul != null && soul.videoClip != null) alreadySet++;
        }

        bool overwriteAll = false;
        if (alreadySet > 0)
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Assign Videos",
                $"{soulGuids.Length} SoulData assets found, {clips.Count} video clip(s) available.\n" +
                $"{alreadySet} soul(s) already have a video assigned.\n\n" +
                "Assign to empty slots only, or randomise all?",
                "Empty Only", "Cancel", "Randomise All");

            if (choice == 1) return;
            if (choice == 2) overwriteAll = true;
        }

        // Build a shuffled clip deck large enough to cover all souls.
        // Repeat the clip list as many times as needed so every soul gets one,
        // distributed as evenly as possible.
        var souls = new List<SoulData>();
        foreach (string guid in soulGuids)
        {
            var soul = AssetDatabase.LoadAssetAtPath<SoulData>(AssetDatabase.GUIDToAssetPath(guid));
            if (soul != null && (overwriteAll || soul.videoClip == null))
                souls.Add(soul);
        }

        if (souls.Count == 0)
        {
            EditorUtility.DisplayDialog("Nothing to Do",
                "All souls already have a video assigned.", "OK");
            return;
        }

        // Build a balanced deck: fill with clips round-robin, then shuffle
        var deck = new List<VideoClip>(souls.Count);
        for (int i = 0; i < souls.Count; i++)
            deck.Add(clips[i % clips.Count]);

        // Fisher-Yates shuffle
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        // Assign
        try
        {
            for (int i = 0; i < souls.Count; i++)
            {
                EditorUtility.DisplayProgressBar(
                    "Assigning Videos",
                    $"{souls[i].soulName} ({i + 1}/{souls.Count})",
                    (float)(i + 1) / souls.Count);

                souls[i].videoClip = deck[i];
                EditorUtility.SetDirty(souls[i]);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"[SoulVideoAssigner] Done — {souls.Count} soul(s) assigned across {clips.Count} clip(s).");
        EditorUtility.DisplayDialog(
            "Videos Assigned",
            $"{souls.Count} soul(s) assigned randomly across {clips.Count} clip(s).",
            "OK");
    }
}
