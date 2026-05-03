using System;
using System.Collections.Generic;

[Serializable]
public class SaveData
{
// ─────────────────────────────────────────────
// BOAT / WORLD MAP
// ─────────────────────────────────────────────
public float boatSplineProgress;
public float riverExtrudeProgress;
public string boatSegmentID = string.Empty;
public bool boatIsLeftPath;
public bool boatIsRightPath; // ← add this

// ─────────────────────────────────────────────
// SETTINGS
// ─────────────────────────────────────────────
public float masterVolume = 1f;
public float ui3DCameraX = float.MaxValue; // sentinel — slider reads camera position on first run

    // ─────────────────────────────────────────────
    // SOULS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Souls currently carried on the boat and available to spend.
    /// </summary>
    public int soulsOnBoat;

    public List<int> soulsOnBoatIdentities  = new List<int>();
    public List<int> soulsOnBoatLevelLocalIDs = new List<int>();

    /// <summary>
    /// Souls permanently deposited at the SoulWell.
    /// </summary>
    public int permanentSouls;

    /// <summary>
    /// Per-level record of which fish (by grid cell index) have been caught.
    /// </summary>
    public List<CaughtSoulsEntry> caughtSouls = new List<CaughtSoulsEntry>();

    // ─────────────────────────────────────────────
    // EXTERNAL WAVE MODIFIER
    // ─────────────────────────────────────────────
    public int externalModifierSoulID = -1;

    // ─────────────────────────────────────────────
    // LEVEL PROGRESS
    // ─────────────────────────────────────────────
    public List<LevelCompletionEntry> levelCompletions = new List<LevelCompletionEntry>();

    // ─────────────────────────────────────────────
    // OBSTACLES
    // ─────────────────────────────────────────────
    public List<string> unlockedObstacles = new List<string>();

    // ─────────────────────────────────────────────
    // EXIT-UNLOCKED RIVER SEGMENTS
    // Segments with ExtrudeOnExit=true that have been permanently unlocked
    // after the player exited an arena. Persisted so they stay extruded on reload.
    // ─────────────────────────────────────────────
    public List<string> exitUnlockedSegments = new List<string>();
}

[Serializable]
public class CaughtSoulsEntry
{
    public string levelID;
    public List<int> caughtLinkIDs = new List<int>();
}

[Serializable]
public class LevelCompletionEntry
{
    public string levelID;
    public int count;
}