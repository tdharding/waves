using UnityEngine;

public class SoulFishIdentityLifetime : MonoBehaviour
{
    LinkIdentityLabel link;
    SoulFishLinkingController linker;
    public int soulDataIdentity;

    // The tagged fish child Transform that was registered as the map marker key
    Transform fishTransform;

    void Awake()
    {
        link = GetComponent<LinkIdentityLabel>();
        linker = FindObjectOfType<SoulFishLinkingController>();

        // Find the child tagged "Fish" — this is the Transform used as the marker key
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.CompareTag("Fish"))
            {
                fishTransform = t;
                break;
            }
        }
    }

    void OnDestroy()
    {
        if (link != null && link.linkID >= 0 && linker != null)
            linker.UnregisterSoulFish(link.linkID);

       if (fishTransform != null)
{
    SoulFishWaveLinker.Unregister(fishTransform);
    SoulFishMapLinker.Unregister(fishTransform);
}
    }
}