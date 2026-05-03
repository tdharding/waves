using UnityEngine;
using System.Collections.Generic;

public class LevelSoulTracker : MonoBehaviour
{
    public static LevelSoulTracker Instance;

    [Header("Runtime")]
    [SerializeField] private int soulsOnBoat;
    public int SoulsOnBoat => soulsOnBoat;

    private string currentLevelID;

    // List A: Map Positions (grid cell index) — used for persistence/respawning
    private readonly List<int> sessionCaughtLinkIDs = new List<int>();

    // List B: Soul Identities (1-100) — used for video/UI identity
    // These two lists are kept in parallel — index N in one matches index N in the other.
    private readonly List<int> sessionCaughtSoulIdentities = new List<int>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ─────────────────────────────────────────────
    // LEVEL INIT
    // ─────────────────────────────────────────────

    public void InitialiseForLevel(string levelID)
    {
        currentLevelID = levelID;
        sessionCaughtLinkIDs.Clear();
        sessionCaughtSoulIdentities.Clear();

        List<int> savedIdentities  = GameProgressData.GetSoulsOnBoatIdentities();
        List<int> savedLevelLocalIDs = GameProgressData.GetSoulsOnBoatLevelLocalIDs();

        if (savedIdentities != null)
        {
            sessionCaughtSoulIdentities.AddRange(savedIdentities);
            for (int i = 0; i < savedIdentities.Count; i++)
                sessionCaughtLinkIDs.Add(i < savedLevelLocalIDs.Count ? savedLevelLocalIDs[i] : -1);
        }

        soulsOnBoat = sessionCaughtSoulIdentities.Count;

        SoulsOnBoatDisplayManager.Instance?.Rebuild();
        Debug.Log($"[LevelSoulTracker] Initialised for '{levelID}'. " +
                  $"Loaded {soulsOnBoat} identities from save.");
    }

    // ─────────────────────────────────────────────
    // ADD
    // ─────────────────────────────────────────────

    public void AddSoulToBoat(int linkID, int soulIdentity)
    {
        soulsOnBoat++;

        if (linkID >= 0 && !sessionCaughtLinkIDs.Contains(linkID))
            sessionCaughtLinkIDs.Add(linkID);
        else
            sessionCaughtLinkIDs.Add(-1); // placeholder to keep lists in sync

        sessionCaughtSoulIdentities.Add(soulIdentity);

        SoulsOnBoatDisplayManager.Instance?.ReturnSoulToDisplay(soulIdentity);

        Debug.Log($"[LevelSoulTracker] Soul #{soulIdentity} added (linkID {linkID}). Total: {soulsOnBoat}");
    }

    // ─────────────────────────────────────────────
    // REMOVE
    // ─────────────────────────────────────────────

    public void RemoveTemporarySoul(int soulIdentity)
    {
        int index = sessionCaughtSoulIdentities.IndexOf(soulIdentity);

        if (index == -1)
        {
            Debug.LogWarning($"[LevelSoulTracker] RemoveTemporarySoul: identity {soulIdentity} not found.");
            return;
        }

        sessionCaughtLinkIDs.RemoveAt(index);
        sessionCaughtSoulIdentities.RemoveAt(index);
        soulsOnBoat = sessionCaughtSoulIdentities.Count;

        GameProgressData.SaveSoulsToBoat(sessionCaughtSoulIdentities, sessionCaughtLinkIDs);

        SoulsOnBoatDisplayManager.Instance?.ConsumeSoulFromDisplay(soulIdentity);

        Debug.Log($"[LevelSoulTracker] Soul #{soulIdentity} removed. Total: {soulsOnBoat}");
    }

    // ─────────────────────────────────────────────
    // LOOKUP
    // ─────────────────────────────────────────────

    /// <summary>
    /// Returns the linkID (grid cell index) for a given soul identity.
    /// Returns -1 if not found or if the soul was loaded from a previous session
    /// (i.e. it was on the boat before this level started).
    /// </summary>
    public int GetLinkIDForIdentity(int soulIdentity)
    {
        int index = sessionCaughtSoulIdentities.IndexOf(soulIdentity);
        if (index == -1 || index >= sessionCaughtLinkIDs.Count) return -1;
        return sessionCaughtLinkIDs[index];
    }

    // ─────────────────────────────────────────────
    // RETURN DROPPED SOULS TO HOME
    // ─────────────────────────────────────────────

    public void ReturnDroppedSoulsToHome()
    {
        int count = 0;

        // Landed souls
        var droppedSnapshot = new List<DroppedSoul>(DroppedSoul.ActiveDroppedSouls);
        foreach (DroppedSoul soul in droppedSnapshot)
        {
            if (soul == null) continue;

            LinkIdentityLabel label = soul.GetComponent<LinkIdentityLabel>();
            if (label == null) continue;

            ReturnSoulToHome(label.soulDataIdentity, label.linkID);
            Destroy(soul.gameObject);
            count++;
        }

        // In-flight projectiles
        var projectileSnapshot = new List<SoulProjectile>(SoulProjectile.ActiveProjectiles);
        foreach (SoulProjectile proj in projectileSnapshot)
        {
            if (proj == null) continue;

            ReturnSoulToHome(proj.IdentityToCarry, proj.LinkIDToCarry);
            Destroy(proj.gameObject);
            count++;
        }

        if (count > 0)
            Debug.Log($"[LevelSoulTracker] {count} soul(s) returned to home ({droppedSnapshot.Count} landed, {projectileSnapshot.Count} in-flight).");
    }

    private void ReturnSoulToHome(int soulIdentity, int linkID)
    {
        SoulData soulData = SoulRegistry.Get(soulIdentity);

        if (soulData != null && !string.IsNullOrEmpty(soulData.homeLevelID) && linkID >= 0)
        {
            GameProgressData.UnrecordCaughtSoul(soulData.homeLevelID, linkID);
            Debug.Log($"[LevelSoulTracker] Soul {soulIdentity} (linkID {linkID}) returned to '{soulData.homeLevelID}'.");
        }
        else
        {
            Debug.LogWarning($"[LevelSoulTracker] Could not return soul {soulIdentity} — " +
                             $"homeLevelID: '{soulData?.homeLevelID}', linkID: {linkID}.");
        }
    }

    // ─────────────────────────────────────────────
    // CLEAR
    // ─────────────────────────────────────────────

    public void ClearTemporarySouls()
    {
        soulsOnBoat = GameProgressData.GetSoulsOnBoat();
        sessionCaughtLinkIDs.Clear();
        sessionCaughtSoulIdentities.Clear();

        SoulsOnBoatDisplayManager.Instance?.Rebuild();
    }

    // ─────────────────────────────────────────────
    // COMMIT
    // ─────────────────────────────────────────────

    public void CommitSouls()
    {
        // Record caught souls for THIS level only
        if (!string.IsNullOrEmpty(currentLevelID))
        {
            foreach (int linkID in sessionCaughtLinkIDs)
            {
                if (linkID >= 0)
                    GameProgressData.RecordCaughtSoul(currentLevelID, linkID);
            }
        }

        // Save full boat contents to persistent state
        GameProgressData.SaveSoulsToBoat(sessionCaughtSoulIdentities, sessionCaughtLinkIDs);
    }

    // ─────────────────────────────────────────────
    // ACCESSORS
    // ─────────────────────────────────────────────

    public List<int> GetAllCaughtIdentities()
    {
        return sessionCaughtSoulIdentities;
    }
}