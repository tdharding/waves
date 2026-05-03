using UnityEngine;
using System.Collections.Generic;

public class SoulsOnBoatDisplayManager : MonoBehaviour
{
    public static SoulsOnBoatDisplayManager Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private GameObject soulIconPrefab;
    [SerializeField] private Transform  iconParent;

    [Header("Slot Layout")]
    [SerializeField] private SoulDisplaySlotManager slotManager;

    // slot index → draggable soul icon
    private readonly Dictionary<int, LevelSelectDraggableSoul> _slots
        = new Dictionary<int, LevelSelectDraggableSoul>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        Rebuild();
    }

    // ─────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────

    /// <summary>Rebuilds display from save data, assigning each soul to its saved slot index.</summary>
    public void Rebuild()
    {
        ClearIcons();

        List<int> identities = GameProgressData.GetSoulsOnBoatIdentities();

        for (int i = 0; i < identities.Count; i++)
            SpawnIconAtSlot(identities[i], i);
    }

    /// <summary>
    /// Returns a specific soul to the display. Finds its slot index from current save data
    /// so order is preserved.
    /// </summary>
    public void ReturnSoulToDisplay(int soulIdentity)
    {
        List<int> identities = GameProgressData.GetSoulsOnBoatIdentities();
        int slotIndex = identities.IndexOf(soulIdentity);
        Debug.Log($"[DisplayManager] ReturnSoulToDisplay({soulIdentity}) — found at identities index={slotIndex}, _slots count={_slots.Count}");

        // If the saved slot index is occupied by a different soul's icon,
        // treat as unplaced — find a free slot instead.
        if (slotIndex >= 0 && _slots.TryGetValue(slotIndex, out var existing))
        {
            if (existing == null)
            {
                Debug.Log($"[DisplayManager] Slot {slotIndex} has stale null entry — clearing.");
                _slots.Remove(slotIndex);
            }
            else if (existing.soulDataIdentity != soulIdentity)
            {
                Debug.Log($"[DisplayManager] Slot {slotIndex} occupied by different soul {existing.soulDataIdentity} — finding free slot.");
                slotIndex = -1;
            }
        }

        if (slotIndex < 0)
        {
            slotIndex = 0;
            while (_slots.TryGetValue(slotIndex, out var taken) && taken != null)
                slotIndex++;
            Debug.Log($"[DisplayManager] Free slot found at {slotIndex}.");
        }

        if (!_slots.ContainsKey(slotIndex))
        {
            Debug.Log($"[DisplayManager] Spawning icon for soul {soulIdentity} at slot {slotIndex}.");
            SpawnIconAtSlot(soulIdentity, slotIndex);
        }
        else
        {
            Debug.LogWarning($"[DisplayManager] Slot {slotIndex} already occupied — icon NOT spawned for soul {soulIdentity}.");
        }
    }

    /// <summary>Removes a specific soul icon by identity.</summary>
    public void ConsumeSoulFromDisplay(int soulIdentity)
    {
        Debug.Log($"[DisplayManager] ConsumeSoulFromDisplay({soulIdentity}) — slots in display: {_slots.Count}");
        foreach (var kvp in _slots)
        {
            if (kvp.Value != null && kvp.Value.soulDataIdentity == soulIdentity)
            {
                Debug.Log($"[DisplayManager] Destroying icon at slot {kvp.Key} for identity {soulIdentity}");
                Destroy(kvp.Value.gameObject);
                _slots.Remove(kvp.Key);
                return;
            }
        }
        Debug.LogWarning($"[DisplayManager] ConsumeSoulFromDisplay({soulIdentity}) — no icon found for that identity!");
    }

    // ─────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────

    private void SpawnIconAtSlot(int soulIdentity, int slotIndex)
    {
        if (soulIconPrefab == null || iconParent == null) return;

        GameObject obj = Instantiate(soulIconPrefab);
        LevelSelectDraggableSoul icon = obj.GetComponent<LevelSelectDraggableSoul>();

        if (icon == null)
        {
            Debug.LogWarning("[SoulsOnBoatDisplayManager] Prefab missing LevelSelectDraggableSoul.");
            Destroy(obj);
            return;
        }

        icon.soulDataIdentity = soulIdentity;

        Vector2 slotPos = slotManager != null
            ? slotManager.GetSlotAnchoredPosition(slotIndex)
            : new Vector2(slotIndex * 68f, 0f); // fallback if no slot manager assigned

        icon.SetHomePosition(iconParent, slotPos);
        icon.transform.SetAsLastSibling();

        _slots[slotIndex] = icon;
    }

    private void ClearIcons()
    {
        foreach (var kvp in _slots)
        {
            if (kvp.Value != null) Destroy(kvp.Value.gameObject);
        }
        _slots.Clear();
    }
}
