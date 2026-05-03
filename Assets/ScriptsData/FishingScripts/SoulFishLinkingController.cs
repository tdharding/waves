using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central authority that links Soul Fish to Reality Proxies
/// using a shared LinkIdentityLabel.linkID.
/// 1-to-1 pairing only.
/// </summary>
public class SoulFishLinkingController : MonoBehaviour
{
    // Registries
    readonly Dictionary<int, Transform> soulFishByID = new();
    readonly Dictionary<int, SoulFishRealityProxyFollow> proxyByID = new();

    // --------------------------------------------------
    // REGISTRATION API
    // --------------------------------------------------
public void RegisterSoulFish(int id, Transform fishRoot)
{
    if (soulFishByID.ContainsKey(id))
    {
        Debug.LogWarning($"Duplicate SoulFish ID detected: {id}", fishRoot);
        return;
    }

    Transform movingFish = FindTaggedFish(fishRoot);

    if (movingFish == null)
    {
        Debug.LogError(
            $"SoulFishLinkingController: No child tagged 'Fish' found for ID {id}",
            fishRoot
        );
        return;
    }

    soulFishByID[id] = movingFish;
    TryLink(id);
}

Transform FindTaggedFish(Transform root)
{
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (t.CompareTag("Fish"))
            return t;
    }

    return null;
}

    public void RegisterRealityProxy(int id, SoulFishRealityProxyFollow proxy)
    {
        if (proxyByID.ContainsKey(id))
        {
            Debug.LogWarning($"Duplicate RealityProxy ID detected: {id}", proxy);
            return;
        }

        proxyByID[id] = proxy;
        TryLink(id);
    }

    // --------------------------------------------------
    // LINK RESOLUTION
    // --------------------------------------------------
    void TryLink(int id)
    {
        if (!soulFishByID.TryGetValue(id, out var fish))
            return;

        if (!proxyByID.TryGetValue(id, out var proxy))
            return;

        proxy.AssignFish(fish);
    }

    // --------------------------------------------------
    // OPTIONAL CLEANUP (safe, but not required yet)
    // --------------------------------------------------
public void UnregisterSoulFish(int id)
{
    soulFishByID.Remove(id);

    if (proxyByID.TryGetValue(id, out var proxy))
    {
        proxy.OnFishDestroyed();
        proxyByID.Remove(id);
    }
}

    public void UnregisterRealityProxy(int id)
    {
        proxyByID.Remove(id);
    }
}
