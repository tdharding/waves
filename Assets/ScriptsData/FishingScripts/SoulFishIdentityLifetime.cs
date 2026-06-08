using UnityEngine;

public class SoulFishIdentityLifetime : MonoBehaviour
{
    LinkIdentityLabel link;
    SoulFishLinkingController linker;
    public int soulDataIdentity;

    void Awake()
    {
        link = GetComponent<LinkIdentityLabel>();
        linker = FindObjectOfType<SoulFishLinkingController>();
    }

    void OnDestroy()
    {
        // Per-fish wave/map unregistration is handled by SoulFishReferencePoint.OnDisable()
        // on each individual fish mesh. Only the shoal-level linker registration is cleaned up here.
        if (link != null && link.linkID >= 0 && linker != null)
            linker.UnregisterSoulFish(link.linkID);
    }
}