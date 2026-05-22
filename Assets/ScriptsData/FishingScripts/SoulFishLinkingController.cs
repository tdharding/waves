using System.Collections.Generic;
using UnityEngine;

public class SoulFishLinkingController : MonoBehaviour
{
    readonly Dictionary<int, Transform> soulFishByID = new();

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

    // --------------------------------------------------
    // CLEANUP
    // --------------------------------------------------
    public void UnregisterSoulFish(int id)
    {
        soulFishByID.Remove(id);
    }
}
