using UnityEngine;

/// <summary>
/// One place to spend a soul that has been accepted by a drop target — a street light, a soul pipe,
/// a gameplay slot or a level-select slot.
///
/// The boat keeps three parallel records of what it is carrying, and all three have to move together
/// or the soul comes back:
///   1. LevelSoulTracker.sessionCaughtSoulIdentities — the live in-level list. BoatCollisionHandler
///      drops from this on a crash, and every tracker write rewrites the save from it, so a stale
///      entry here both lets a spent soul be dropped and re-caught, and resurrects it in save data.
///   2. The soul visuals under FishingController.soulsParent — the souls the player can see on deck.
///   3. GameProgressData + the SoulsOnBoatDisplayManager icons.
/// </summary>
public static class SoulConsumption
{
    /// <summary>
    /// Removes a soul from the boat for good, after a target has accepted it.
    /// Safe to call in level select, where there is no LevelSoulTracker or boat.
    /// </summary>
    public static void SpendSoulFromBoat(int soulIdentity)
    {
        LevelSoulTracker tracker = LevelSoulTracker.Instance;
        bool trackerHasSoul = tracker != null
                           && tracker.GetAllCaughtIdentities() != null
                           && tracker.GetAllCaughtIdentities().Contains(soulIdentity);

        if (trackerHasSoul)
        {
            // Rewrites the save from the live list and clears the display icon itself.
            tracker.RemoveTemporarySoul(soulIdentity);
        }
        else
        {
            // Level select, or a soul the tracker never knew about.
            GameProgressData.RemoveSoulFromBoat(soulIdentity);
            SoulsOnBoatDisplayManager.Instance?.ConsumeSoulFromDisplay(soulIdentity);
        }

        LevelDataController.Instance?.FishingController?.ConsumeSoulVisual();

        Debug.Log($"[SoulConsumption] Soul {soulIdentity} spent — tracker={(trackerHasSoul ? "removed" : "not tracked")}, save + display + boat visual updated.");
    }
}
