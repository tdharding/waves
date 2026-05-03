using UnityEngine;
using System.IO;

public static class SaveManager
{
    private static readonly string SavePath = Path.Combine(Application.persistentDataPath, "save.json");

    private static SaveData _cache;

    public static SaveData Load()
    {
        if (_cache != null) return _cache;

        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);
                _cache = JsonUtility.FromJson<SaveData>(json);
                Debug.Log($"[SaveManager] Save loaded from {SavePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SaveManager] Failed to load save file, starting fresh. Error: {e.Message}");
                _cache = new SaveData();
            }
        }
        else
        {
            Debug.Log("[SaveManager] No save file found, starting fresh.");
            _cache = new SaveData();
        }

        return _cache;
    }

    public static void Write()
    {
        if (_cache == null) return;

        try
        {
            string json = JsonUtility.ToJson(_cache, prettyPrint: true);
            File.WriteAllText(SavePath, json);
            Debug.Log($"[SaveManager] Save written to {SavePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveManager] Failed to write save file. Error: {e.Message}");
        }
    }

    public static void ClearAll()
    {
        _cache = new SaveData();
        if (File.Exists(SavePath))
        {
            File.Delete(SavePath);
        }
        Debug.Log("[SaveManager] Save data cleared.");
    }
}