using UnityEngine;
using UnityEditor;
using UnityEngine.Video;
using System.IO;
using System.Collections.Generic;

public class SoulDataGenerator : EditorWindow
{
    // Resources/Souls is where the game reads souls from (SoulRegistry, VideoPlayerController,
    // TestSoulInjectorUI all use Resources.LoadAll<SoulData>("Souls")), so souls written
    // anywhere else are invisible at runtime.
    private string savePath = "Assets/Resources/Souls";
    private string videoPath = "Assets/Resources/Videos";
    private string assetBaseName = "SoulData_";
    private int soulCount = 100;

    [MenuItem("Tools/Soul System/Generate Souls")]
    public static void ShowWindow()
    {
        GetWindow<SoulDataGenerator>("Soul Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Soul Data Bulk Generator", EditorStyles.boldLabel);

        savePath = EditorGUILayout.TextField("Save Destination", savePath);
        videoPath = EditorGUILayout.TextField("Video Source Folder", videoPath);
        assetBaseName = EditorGUILayout.TextField("Base File Name", assetBaseName);
        soulCount = EditorGUILayout.IntField("Number of Souls", soulCount);
        soulCount = Mathf.Max(1, soulCount);

        if (GUILayout.Button($"Generate {soulCount} Soul Assets"))
        {
            GenerateSouls();
        }
    }

    private void GenerateSouls()
    {
        // 1. Ensure Directory Exists
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        // 2. Find Videos in the source folder
        string[] videoFiles = Directory.GetFiles(videoPath, "*.mp4"); // Adjust extension if needed
        if (videoFiles.Length == 0)
        {
            Debug.LogError($"No videos found in {videoPath}. Please check the path.");
            return;
        }

        List<VideoClip> clips = new List<VideoClip>();
        foreach (string path in videoFiles)
        {
            VideoClip clip = AssetDatabase.LoadAssetAtPath<VideoClip>(path);
            if (clip != null) clips.Add(clip);
        }

        // 3. Generation Loop
        int created = 0, skipped = 0;
        for (int i = 1; i <= soulCount; i++)
        {
            SoulData newSoul = ScriptableObject.CreateInstance<SoulData>();

            // Assign Identity (1-N)
            newSoul.soulDataIdentity = i;
            newSoul.soulName = $"Soul #{i}";

            // Assign Video (Alternating between available clips)
            if (clips.Count > 0)
            {
                newSoul.videoClip = clips[(i - 1) % clips.Count];
            }

            // Save the Asset
            string fullPath = $"{savePath}/{assetBaseName}{i:000}.asset";

            // Never overwrite a soul that already exists: CreateAsset would replace it with a new
            // GUID, and every level slot pointing at the old one would come back as a missing
            // reference.
            if (AssetDatabase.LoadAssetAtPath<SoulData>(fullPath) != null)
            {
                skipped++;
                continue;
            }

            AssetDatabase.CreateAsset(newSoul, fullPath);
            created++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        Debug.Log($"Generated {created} new SoulData asset(s) in {savePath}. Left {skipped} existing asset(s) untouched.");
    }
}