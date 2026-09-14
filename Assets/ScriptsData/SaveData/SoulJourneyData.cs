using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single answer to "where is soul N?".
///
/// Soul location used to be spread across three records in two ID spaces: caughtSouls by linkID,
/// the boat lists by identity (with a parallel list purely to translate between the two), and
/// allocated/homeLevelID stamped on the shared SoulData asset. None of them can express a soul
/// that is between levels, and the third cannot differ per save slot because there is only one
/// asset. This holds the place instead.
///
/// SCOPE — what this does NOT own:
///   • The boat. LevelSoulTracker still owns who is aboard and commits the boat lists on exit;
///     a soul's place here reads OnBoat, and the details stay where they already work.
///   • Movement. Nothing here advances a soul on its own; callers do, then Flush.
///   • Rendering. The branch-water controller reads Travelling() each frame and owns no state.
///   • Route authoring. routeId/legIndex are recorded, not interpreted.
///
/// Keyed on soulDataIdentity — the only ID that stays true when a soul moves between levels.
/// linkID is a spawn slot (zoneIndex * 100 + i), not an identity, and is deliberately absent.
/// </summary>
public static class SoulJourneyData
{
    // ─────────────────────────────────────────────
    // LOOKUP
    // ─────────────────────────────────────────────

    /// <summary>
    /// The soul's current record, creating one at the origin pool if it has never been placed.
    /// Never returns null, so callers do not have to guard every read.
    /// </summary>
    public static SoulJourneyEntry Where(int identity)
    {
        var data  = SaveManager.Load();
        var entry = data.soulJourneys.Find(e => e.identity == identity);

        if (entry == null)
        {
            entry = new SoulJourneyEntry { identity = identity, place = SoulPlace.OriginPool };
            data.soulJourneys.Add(entry);
            SaveManager.Write();
        }

        return entry;
    }

    /// <summary>True if this soul has ever been placed. Use to tell "unplaced" from "in the pool".</summary>
    public static bool HasRecord(int identity)
    {
        return SaveManager.Load().soulJourneys.Exists(e => e.identity == identity);
    }

    /// <summary>Identities currently swimming in <paramref name="levelID"/> — what that level spawns.</summary>
    public static List<int> SoulsIn(string levelID)
    {
        var result = new List<int>();
        if (string.IsNullOrEmpty(levelID)) return result;

        foreach (var entry in SaveManager.Load().soulJourneys)
            if (entry.place == SoulPlace.InLevel && entry.levelID == levelID)
                result.Add(entry.identity);

        return result;
    }

    /// <summary>
    /// Identities swimming in <paramref name="levelID"/> that arrived by a given door. A soul zone
    /// pinned to the entrances begins at one, so this is the list that zone spawns on top of its
    /// authored souls.
    /// </summary>
    public static List<int> SoulsIn(string levelID, int entranceIndex)
    {
        var result = new List<int>();
        if (string.IsNullOrEmpty(levelID) || entranceIndex < 0) return result;

        foreach (var entry in SaveManager.Load().soulJourneys)
            if (entry.place == SoulPlace.InLevel &&
                entry.levelID == levelID &&
                entry.arrivedEntranceIndex == entranceIndex)
                result.Add(entry.identity);

        return result;
    }

    /// <summary>
    /// Every soul currently on the river. The branch-water controller turns these into positions
    /// and publishes them; the 32-fish shader ceiling limits what is DRAWN, never what exists.
    /// </summary>
    public static List<SoulJourneyEntry> Travelling()
    {
        var result = new List<SoulJourneyEntry>();

        foreach (var entry in SaveManager.Load().soulJourneys)
            if (entry.place == SoulPlace.OnRiver)
                result.Add(entry);

        return result;
    }

    // ─────────────────────────────────────────────
    // MOVES
    // ─────────────────────────────────────────────

    /// <summary>
    /// Parks a soul in a route's origin pool — the head of its journey, before anything has
    /// released it. Used when a route's authored pool is first read.
    /// </summary>
    public static void PlaceInOriginPool(int identity, string routeId)
    {
        var entry = Where(identity);

        entry.place    = SoulPlace.OriginPool;
        entry.routeId  = routeId ?? string.Empty;
        entry.legIndex = -1;
        entry.progress = 0f;
        entry.levelID  = string.Empty;

        SaveManager.Write();
    }

    /// <summary>
    /// Puts a soul onto the river at the head of <paramref name="legIndex"/>. This is what a zone
    /// reaching its door does, and what toppling a fish-bowl tower or destroying a statue does —
    /// the soul is released into the stream and travels on toward its destination.
    /// </summary>
    public static void Release(int identity, string routeId, int legIndex)
    {
        var entry = Where(identity);

        entry.place    = SoulPlace.OnRiver;
        entry.routeId  = routeId ?? string.Empty;
        entry.legIndex = legIndex;
        entry.progress = 0f;
        entry.levelID  = string.Empty;

        SaveManager.Write();
        Debug.Log($"[SoulJourney] Soul #{identity} released onto route '{routeId}' leg {legIndex}.");
    }

    /// <summary>
    /// Moves a travelling soul along its current leg. Deliberately does NOT write to disk — this
    /// is called every frame while fish swim, and a per-frame file write would be brutal. The
    /// caller flushes at a sensible moment (leaving the scene, arriving, saving).
    /// </summary>
    public static void Advance(int identity, float progress)
    {
        var entry = Where(identity);
        if (entry.place != SoulPlace.OnRiver) return;

        entry.progress = Mathf.Clamp01(progress);
    }

    /// <summary>
    /// A soul swims out of a level's door. Called from inside the ARENA scene, which cannot see
    /// the level-select routes — so it records the door and leaves the route unresolved. The
    /// level-select scene turns that into a route position when it loads, via Unresolved().
    /// </summary>
    public static void DepartLevel(int identity, string levelID, int entranceIndex)
    {
        var entry = Where(identity);

        entry.place                = SoulPlace.OnRiver;
        entry.routeId              = string.Empty;
        entry.legIndex             = -1;
        entry.progress             = 0f;
        entry.levelID              = string.Empty;
        entry.arrivedEntranceIndex = -1;
        entry.fromLevelID          = levelID ?? string.Empty;
        entry.fromEntranceIndex    = entranceIndex;

        SaveManager.Write();
        Debug.Log($"[SoulJourney] Soul #{identity} left '{levelID}' by door {entranceIndex} — route unresolved.");
    }

    /// <summary>
    /// Souls that are on the river but whose route has not been worked out yet. The level-select
    /// scene resolves these on load and then calls Release to place them properly.
    /// </summary>
    public static List<SoulJourneyEntry> Unresolved()
    {
        var result = new List<SoulJourneyEntry>();

        foreach (var entry in SaveManager.Load().soulJourneys)
            if (entry.place == SoulPlace.OnRiver && string.IsNullOrEmpty(entry.routeId))
                result.Add(entry);

        return result;
    }

    /// <summary>Moves a travelling soul onto the next leg of its route, at the start of it.</summary>
    public static void EnterLeg(int identity, int legIndex)
    {
        var entry = Where(identity);
        if (entry.place != SoulPlace.OnRiver) return;

        entry.legIndex = legIndex;
        entry.progress = 0f;
        SaveManager.Write();
    }

    /// <summary>
    /// The soul reaches a level and joins its soul zone — catchable from here, per the rule that
    /// a fish in a zone is catchable unless its zone says otherwise.
    /// </summary>
    public static void Arrive(int identity, string levelID, int entranceIndex)
    {
        var entry = Where(identity);

        entry.place                = SoulPlace.InLevel;
        entry.levelID              = levelID ?? string.Empty;
        entry.arrivedEntranceIndex = entranceIndex;
        entry.legIndex             = -1;
        entry.progress             = 0f;

        SaveManager.Write();
        Debug.Log($"[SoulJourney] Soul #{identity} arrived in '{levelID}' by door {entranceIndex}.");
    }

    /// <summary>
    /// The soul is caught and carried. LevelSoulTracker still owns the boat contents; this only
    /// records that the soul is no longer in a level or on the river.
    /// </summary>
    public static void Catch(int identity)
    {
        var entry = Where(identity);

        entry.place                = SoulPlace.OnBoat;
        entry.levelID              = string.Empty;
        entry.arrivedEntranceIndex = -1;
        entry.legIndex = -1;
        entry.progress = 0f;

        SaveManager.Write();
    }

    /// <summary>Commits any un-written changes — notably those left by Advance.</summary>
    public static void Flush() => SaveManager.Write();

    // ─────────────────────────────────────────────
    // ROUTE OPENNESS
    // Shared player progress, not per-soul: how far down a route the way is clear.
    // ─────────────────────────────────────────────

    /// <summary>Index of the last opened leg of <paramref name="routeId"/>. -1 = nothing opened.</summary>
    public static int OpenLegOf(string routeId)
    {
        if (string.IsNullOrEmpty(routeId)) return -1;
        var entry = SaveManager.Load().routeProgress.Find(e => e.routeId == routeId);
        return entry?.openLegIndex ?? -1;
    }

    /// <summary>
    /// Opens the route as far as <paramref name="legIndex"/>. Only ever moves forward, so a
    /// re-fired departure gate cannot wind a route back on itself.
    /// </summary>
    public static void OpenRouteTo(string routeId, int legIndex)
    {
        if (string.IsNullOrEmpty(routeId)) return;

        var data  = SaveManager.Load();
        var entry = data.routeProgress.Find(e => e.routeId == routeId);

        if (entry == null)
        {
            entry = new RouteOpenEntry { routeId = routeId, openLegIndex = -1 };
            data.routeProgress.Add(entry);
        }

        if (legIndex <= entry.openLegIndex) return;

        entry.openLegIndex = legIndex;
        SaveManager.Write();
        Debug.Log($"[SoulJourney] Route '{routeId}' opened to leg {legIndex}.");
    }

    // ─────────────────────────────────────────────
    // MAINTENANCE
    // ─────────────────────────────────────────────

    /// <summary>Wipes every journey record. For the tester tool and experimental resets.</summary>
    public static void ClearAllJourneys()
    {
        var data = SaveManager.Load();
        int count = data.soulJourneys.Count;
        data.soulJourneys.Clear();
        SaveManager.Write();
        Debug.Log($"[SoulJourney] Cleared {count} journey record(s).");
    }
}
