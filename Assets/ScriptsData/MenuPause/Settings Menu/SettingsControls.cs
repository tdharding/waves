using UnityEngine;

public static class SettingsControls
{
    // ─────────────────────────────────────────────
    // SETTINGS — VOLUME
    // ─────────────────────────────────────────────

    public static float GetMasterVolume()
    {
        return SaveManager.Load().masterVolume;
    }

    public static void SetMasterVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        SaveManager.Load().masterVolume = volume;
        SaveManager.Write();
        ApplyMasterVolume();
    }

    public static float GetUI3DCameraX()
{
    return SaveManager.Load().ui3DCameraX;
}

public static void SetUI3DCameraX(float value)
{
    SaveManager.Load().ui3DCameraX = value;
    SaveManager.Write();
}

    /// <summary>
    /// Call once on startup to restore the player's saved volume.
    /// </summary>
    public static void ApplyMasterVolume()
    {
        AudioListener.volume = SaveManager.Load().masterVolume;
    }
}