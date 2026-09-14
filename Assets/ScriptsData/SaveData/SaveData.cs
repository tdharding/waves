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

// Where the boat actually stood when the map was last left. The segment and progress above
// still name a place — a door says which river it lets out onto — but the boat is no longer
// on rails, so where it got to is a position and a heading. A pose written here wins; a door
// writing a segment clears it, because the door is saying where to come out.
public bool    boatHasPose;
public float   boatPoseX;
public float   boatPoseY;
public float   boatPoseZ;
public float   boatPoseHeading;

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
    /// Orbs of Omalon collected and available for spending.
    /// </summary>
    public int collectedOrbs;

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
    // SHOP PURCHASES
    // IDs of items bought from shops (figurehead, etc.). Permanent.
    // ─────────────────────────────────────────────
    public List<string> purchasedShopItems = new List<string>();

    // ─────────────────────────────────────────────
    // EXIT-UNLOCKED RIVER SEGMENTS
    // Segments with ExtrudeOnExit=true that have been permanently unlocked
    // after the player exited an arena. Persisted so they stay extruded on reload.
    // ─────────────────────────────────────────────
    public List<string> exitUnlockedSegments = new List<string>();

    // ─────────────────────────────────────────────
    // SOUL JOURNEYS
    // Where each soul is on its journey across the map. Keyed by soulDataIdentity —
    // the only ID that stays true when a soul moves between levels. Additive: the boat
    // lists and caughtSouls above are untouched and still authoritative for what they own.
    // ─────────────────────────────────────────────
    public List<SoulJourneyEntry> soulJourneys = new List<SoulJourneyEntry>();

    /// <summary>
    /// How far each authored soul route has been opened by the player.
    /// </summary>
    public List<RouteOpenEntry> routeProgress = new List<RouteOpenEntry>();
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
/// <summary>
/// Where one soul is, right now. One record per soul, so its place can never be
/// two things at once — and so a soul that has LEFT a level is recorded as absent
/// rather than merely failing to appear in that level's caught list.
/// </summary>
[Serializable]
public class SoulJourneyEntry
{
    /// <summary>SoulData.soulDataIdentity. The key.</summary>
    public int identity;

    public SoulPlace place = SoulPlace.OriginPool;

    /// <summary>InLevel: the GridData.levelID the soul is swimming in.</summary>
    public string levelID = string.Empty;

    /// <summary>Which authored route the soul travels. Empty until routes exist.</summary>
    public string routeId = string.Empty;

    /// <summary>OnRiver: index of the leg of that route the soul currently occupies.</summary>
    public int legIndex = -1;

    /// <summary>OnRiver: 0-1 along that leg.</summary>
    public float progress;

    // ── Unresolved departure ──────────────────────────
    // A soul leaves a level from inside the ARENA scene, which cannot see the level-select
    // routes. So the arena records only the door it left by; the level-select scene resolves
    // that into a route and leg when it loads. While routeId is empty and place is OnRiver,
    // these two say where the soul came out.

    /// <summary>The level this soul left, while its route is still unresolved.</summary>
    public string fromLevelID = string.Empty;

    /// <summary>Index into that level's GridData.entrances for the door it left by. -1 = none.</summary>
    public int fromEntranceIndex = -1;

    // ── Arrival ───────────────────────────────────
    // A soul zone that is pinned to the doors begins AT an entrance, so the door a soul
    // swam in by is the zone it joins. Recorded on arrival; read by LevelSpawner.

    /// <summary>InLevel: index into that level's GridData.entrances for the door it swam in by. -1 = none.</summary>
    public int arrivedEntranceIndex = -1;
}

/// <summary>
/// The places a soul can be. One value, not a set of flags, so the states cannot contradict.
/// </summary>
public enum SoulPlace
{
    /// <summary>Waiting at the head of its route, not yet released into the world.</summary>
    OriginPool = 0,

    /// <summary>Swimming a soul zone inside a level. Catchable, unless its zone gates it.</summary>
    InLevel = 1,

    /// <summary>Travelling the level-select river between levels.</summary>
    OnRiver = 2,

    /// <summary>Caught and carried. The boat lists remain authoritative for the details.</summary>
    OnBoat = 3
}

/// <summary>
/// How far one route has been opened. Souls travel freely up to the end of this leg and
/// gather where it stops, the way they gather at an unlit lamp inside a level.
/// </summary>
[Serializable]
public class RouteOpenEntry
{
    public string routeId = string.Empty;

    /// <summary>Index of the last opened leg. -1 = nothing opened yet.</summary>
    public int openLegIndex = -1;
}
