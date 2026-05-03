using UnityEngine;

public class OrbCollectCounter : MonoBehaviour
{
    public static int CollectedCount { get; private set; }

    public static void ResetCount()
    {
        CollectedCount = 0;
    }

    public static void AddOrb()
    {
        CollectedCount++;
        Debug.Log($"[OrbCollectCounter] Orbs collected: {CollectedCount}");
    }
}