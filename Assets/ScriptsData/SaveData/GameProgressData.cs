using UnityEngine;
using System.Collections.Generic;

public static class GameProgressData
{
    // ─────────────────────────────────────────────
    // OBSTACLE UNLOCKS
    // ─────────────────────────────────────────────

    public static void UnlockObstacle(string obstacleID)
    {
        if (string.IsNullOrEmpty(obstacleID)) return;
        var data = SaveManager.Load();
        if (!data.unlockedObstacles.Contains(obstacleID))
        {
            data.unlockedObstacles.Add(obstacleID);
            SaveManager.Write();
        }
    }

    public static bool IsUnlocked(string obstacleID)
    {
        if (string.IsNullOrEmpty(obstacleID)) return false;
        return SaveManager.Load().unlockedObstacles.Contains(obstacleID);
    }

    public static void LockObstacle(string obstacleID)
    {
        if (string.IsNullOrEmpty(obstacleID)) return;
        var data = SaveManager.Load();
        if (data.unlockedObstacles.Remove(obstacleID))
            SaveManager.Write();
    }

    public static void ClearUnlocks()
    {
        SaveManager.Load().unlockedObstacles.Clear();
        SaveManager.Write();
    }

    // ─────────────────────────────────────────────
    // LEVEL COMPLETION COUNTS
    // ─────────────────────────────────────────────

    public static int GetCompletionCount(string levelID)
    {
        if (string.IsNullOrEmpty(levelID)) return 0;
        return GetCompletionEntry(levelID)?.count ?? 0;
    }

    public static void IncrementCompletionCount(string levelID)
    {
        if (string.IsNullOrEmpty(levelID)) return;
        var data  = SaveManager.Load();
        var entry = GetCompletionEntry(levelID);
        if (entry == null)
        {
            entry = new LevelCompletionEntry { levelID = levelID, count = 0 };
            data.levelCompletions.Add(entry);
        }
        entry.count++;
        SaveManager.Write();
    }

    public static void ClearCompletionCount(string levelID)
    {
        if (string.IsNullOrEmpty(levelID)) return;
        var data = SaveManager.Load();
        data.levelCompletions.RemoveAll(e => e.levelID == levelID);
        SaveManager.Write();
    }

    private static LevelCompletionEntry GetCompletionEntry(string levelID)
    {
        return SaveManager.Load().levelCompletions.Find(e => e.levelID == levelID);
    }

    // ─────────────────────────────────────────────
    // BOAT SPLINE PROGRESS
    // ─────────────────────────────────────────────

    public static float GetBoatProgress(float defaultValue = 0f)
    {
        float saved = SaveManager.Load().boatSplineProgress;
        return saved == 0f ? defaultValue : saved;
    }

    public static void SaveBoatProgress(float progress)
    {
        SaveManager.Load().boatSplineProgress = progress;
        SaveManager.Write();
    }

    public static string GetBoatSegmentID()
    {
        return SaveManager.Load().boatSegmentID;
    }

    public static void SaveBoatSegmentID(string segmentID)
    {
        SaveManager.Load().boatSegmentID = segmentID;
        SaveManager.Write();
    }

    public static bool GetBoatIsLeftPath()
    {
        return SaveManager.Load().boatIsLeftPath;
    }

    public static bool GetBoatIsRightPath()
    {
        return SaveManager.Load().boatIsRightPath;
    }

    public static float GetRiverProgress(float defaultValue = 0f)
    {
        float saved = SaveManager.Load().riverExtrudeProgress;
        return saved == 0f ? defaultValue : saved;
    }

    public static void SaveRiverProgress(float progress)
    {
        SaveManager.Load().riverExtrudeProgress = progress;
        SaveManager.Write();
    }

    public static void SaveBoatState(string segmentID, float progress,
                                     bool isLeftPath = false, bool isRightPath = false)
    {
        var data            = SaveManager.Load();
        data.boatSegmentID      = segmentID;
        data.boatSplineProgress = progress;
        data.boatIsLeftPath      = isLeftPath;
        data.boatIsRightPath   = isRightPath;
        SaveManager.Write();
    }

    public static void ClearBoatProgress()
    {
        var data            = SaveManager.Load();
        data.boatSplineProgress = 0f;
        data.boatSegmentID      = string.Empty;
        data.boatIsLeftPath      = false;
        data.boatIsRightPath   = false;
        SaveManager.Write();
    }

    // ─────────────────────────────────────────────
    // CAUGHT SOULS (per level, by grid cell index)
    // ─────────────────────────────────────────────

    public static bool IsSoulCaught(string levelID, int linkID)
    {
        if (string.IsNullOrEmpty(levelID)) return false;
        var entry = GetCaughtSoulsEntry(levelID);
        return entry != null && entry.caughtLinkIDs.Contains(linkID);
    }

    public static void RecordCaughtSoul(string levelID, int linkID)
    {
        if (string.IsNullOrEmpty(levelID) || linkID < 0) return;

        var data  = SaveManager.Load();
        var entry = GetCaughtSoulsEntry(levelID);

        if (entry == null)
        {
            entry = new CaughtSoulsEntry { levelID = levelID };
            data.caughtSouls.Add(entry);
        }

        if (!entry.caughtLinkIDs.Contains(linkID))
        {
            entry.caughtLinkIDs.Add(linkID);
            SaveManager.Write();
        }
    }

    /// <summary>
    /// Removes a soul's caught record from its home level.
    /// Called when a dropped soul is left unrescued on level exit,
    /// so it respawns correctly on the next visit to its home level.
    /// </summary>
    public static void UnrecordCaughtSoul(string levelID, int linkID)
    {
        if (string.IsNullOrEmpty(levelID) || linkID < 0) return;

        var entry = GetCaughtSoulsEntry(levelID);
        if (entry == null) return;

        if (entry.caughtLinkIDs.Remove(linkID))
        {
            SaveManager.Write();
            Debug.Log($"[GameProgressData] Unrecorded soul linkID {linkID} from level '{levelID}'. " +
                      $"Will respawn on next visit.");
        }
    }

    public static List<int> GetCaughtSoulIDs(string levelID)
    {
        if (string.IsNullOrEmpty(levelID)) return new List<int>();
        return GetCaughtSoulsEntry(levelID)?.caughtLinkIDs ?? new List<int>();
    }

    private static CaughtSoulsEntry GetCaughtSoulsEntry(string levelID)
    {
        return SaveManager.Load().caughtSouls.Find(e => e.levelID == levelID);
    }

    // ─────────────────────────────────────────────
    // SOULS ON BOAT
    // ─────────────────────────────────────────────

    public static int GetSoulsOnBoat()
    {
        return SaveManager.Load().soulsOnBoat;
    }

    public static void SaveSoulsToBoat(List<int> soulIDs, List<int> levelLocalIDs = null)
    {
        var data                      = SaveManager.Load();
        data.soulsOnBoat              = soulIDs.Count;
        data.soulsOnBoatIdentities    = new List<int>(soulIDs);
        data.soulsOnBoatLevelLocalIDs = levelLocalIDs != null
            ? new List<int>(levelLocalIDs)
            : new List<int>(new int[soulIDs.Count]); // default -0, will be overwritten with -1 logic at load
        SaveManager.Write();
    }

    public static List<int> GetSoulsOnBoatIdentities()
    {
        return SaveManager.Load().soulsOnBoatIdentities ?? new List<int>();
    }

    public static List<int> GetSoulsOnBoatLevelLocalIDs()
    {
        return SaveManager.Load().soulsOnBoatLevelLocalIDs ?? new List<int>();
    }

    public static void ClearSoulsOnBoat()
    {
        var data = SaveManager.Load();
        data.soulsOnBoat = 0;
        data.soulsOnBoatIdentities.Clear();
        data.soulsOnBoatLevelLocalIDs.Clear();
        SaveManager.Write();
    }

    /// <summary>Removes a specific soul from the boat list (e.g. moved into a modifier slot).</summary>
    public static void RemoveSoulFromBoat(int soulIdentity)
    {
        var data = SaveManager.Load();
        int idx = data.soulsOnBoatIdentities.IndexOf(soulIdentity);
        if (idx < 0) return;

        data.soulsOnBoatIdentities.RemoveAt(idx);
        if (idx < data.soulsOnBoatLevelLocalIDs.Count)
            data.soulsOnBoatLevelLocalIDs.RemoveAt(idx);
        data.soulsOnBoat = data.soulsOnBoatIdentities.Count;
        SaveManager.Write();
    }

    /// <summary>Adds a specific soul back to the boat list (e.g. returned from a modifier slot).</summary>
    public static void AddSoulToBoat(int soulIdentity)
    {
        var data = SaveManager.Load();
        data.soulsOnBoatIdentities.Add(soulIdentity);
        data.soulsOnBoatLevelLocalIDs.Add(-1);
        data.soulsOnBoat = data.soulsOnBoatIdentities.Count;
        SaveManager.Write();
    }

    public static bool SpendSouls(int amount)
    {
        if (amount <= 0) return false;
        var data = SaveManager.Load();
        if (data.soulsOnBoat < amount) return false;

        data.soulsOnBoat -= amount;
        for (int i = 0; i < amount; i++)
        {
            if (data.soulsOnBoatIdentities.Count > 0)
                data.soulsOnBoatIdentities.RemoveAt(data.soulsOnBoatIdentities.Count - 1);
            if (data.soulsOnBoatLevelLocalIDs.Count > 0)
                data.soulsOnBoatLevelLocalIDs.RemoveAt(data.soulsOnBoatLevelLocalIDs.Count - 1);
        }

        SaveManager.Write();
        return true;
    }

    // ─────────────────────────────────────────────
    // PERMANENT SOULS
    // ─────────────────────────────────────────────

    public static int GetPermanentSouls()
    {
        return SaveManager.Load().permanentSouls;
    }

    public static void AddPermanentSouls(int amount)
    {
        if (amount <= 0) return;
        SaveManager.Load().permanentSouls += amount;
        SaveManager.Write();
    }

    // ─────────────────────────────────────────────
    // ORBS
    // ─────────────────────────────────────────────

    public static int GetCollectedOrbs()
    {
        return SaveManager.Load().collectedOrbs;
    }

    public static void AddOrbs(int amount)
    {
        if (amount <= 0) return;
        var data = SaveManager.Load();
        data.collectedOrbs += amount;
        SaveManager.Write();
    }

    public static bool SpendOrbs(int amount)
    {
        if (amount <= 0) return false;
        var data = SaveManager.Load();
        if (data.collectedOrbs < amount) return false;

        data.collectedOrbs -= amount;
        SaveManager.Write();
        return true;
    }

    // ─────────────────────────────────────────────
    // EXTERNAL WAVE MODIFIER
    // ─────────────────────────────────────────────

    public static int GetExternalModifierSoulID()
    {
        return SaveManager.Load().externalModifierSoulID;
    }

    public static void SaveExternalModifierSoulID(int soulID)
    {
        SaveManager.Load().externalModifierSoulID = soulID;
        SaveManager.Write();
    }

    // ─────────────────────────────────────────────
    // SHOP PURCHASES
    // ─────────────────────────────────────────────

    /// <summary>Fired when an item is bought (or revoked in the tester tool),
    /// so anything already in the scene can react immediately.</summary>
    public static event System.Action<string, bool> ShopItemOwnershipChanged;

    public static bool IsItemPurchased(string itemID)
    {
        if (string.IsNullOrEmpty(itemID)) return false;
        return SaveManager.Load().purchasedShopItems.Contains(itemID);
    }

    public static void PurchaseItem(string itemID)
    {
        if (string.IsNullOrEmpty(itemID)) return;
        var data = SaveManager.Load();
        if (!data.purchasedShopItems.Contains(itemID))
        {
            data.purchasedShopItems.Add(itemID);
            SaveManager.Write();
        }
        ShopItemOwnershipChanged?.Invoke(itemID, true);
    }

    /// <summary>Undoes a purchase. Used by the tester tool.</summary>
    public static void RevokeItem(string itemID)
    {
        if (string.IsNullOrEmpty(itemID)) return;
        var data = SaveManager.Load();
        if (data.purchasedShopItems.Remove(itemID))
            SaveManager.Write();
        ShopItemOwnershipChanged?.Invoke(itemID, false);
    }

    public static void ClearPurchasedItems()
    {
        SaveManager.Load().purchasedShopItems.Clear();
        SaveManager.Write();
    }

    // ─────────────────────────────────────────────
    // EXIT-UNLOCKED RIVER SEGMENTS
    // ─────────────────────────────────────────────

    public static bool IsSegmentExitUnlocked(string segmentID)
    {
        if (string.IsNullOrEmpty(segmentID)) return false;
        return SaveManager.Load().exitUnlockedSegments.Contains(segmentID);
    }

    public static void UnlockSegmentOnExit(string segmentID)
    {
        if (string.IsNullOrEmpty(segmentID)) return;
        var data = SaveManager.Load();
        if (!data.exitUnlockedSegments.Contains(segmentID))
        {
            data.exitUnlockedSegments.Add(segmentID);
            SaveManager.Write();
        }
    }

    // ─────────────────────────────────────────────
    // CLEAR ALL
    // ─────────────────────────────────────────────

    public static void ClearAll()
    {
        SaveManager.ClearAll();
    }
}