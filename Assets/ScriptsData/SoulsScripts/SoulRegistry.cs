using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Lightweight DontDestroyOnLoad singleton that provides identity → SoulData lookup.
/// Replaces SoulDistributor — no distribution logic, just a registry.
/// Place on a persistent GameObject in your boot/main scene.
/// Requires: Resources/Souls/ — all SoulData assets.
/// </summary>
public class SoulRegistry : MonoBehaviour
{
    public static SoulRegistry Instance { get; private set; }

    private readonly Dictionary<int, SoulData> _registry = new Dictionary<int, SoulData>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        foreach (var soul in Resources.LoadAll<SoulData>("Souls"))
        {
            if (soul == null) continue;
            if (!_registry.ContainsKey(soul.soulDataIdentity))
                _registry[soul.soulDataIdentity] = soul;
            else
                Debug.LogWarning($"[SoulRegistry] Duplicate soulDataIdentity {soul.soulDataIdentity} on '{soul.name}' — skipping.");
        }

        Debug.Log($"[SoulRegistry] Built — {_registry.Count} souls loaded.");
    }

    /// <summary>Returns the SoulData for a given identity. Null if not found.</summary>
    public static SoulData Get(int identity)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[SoulRegistry] No instance — is SoulRegistry in your boot scene?");
            return null;
        }
        Instance._registry.TryGetValue(identity, out SoulData result);
        return result;
    }
}
