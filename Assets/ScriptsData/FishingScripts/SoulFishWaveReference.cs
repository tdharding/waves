using UnityEngine;

public class SoulFishWaveReference : MonoBehaviour
{
    [Tooltip("Registers this fish's position with the wave distortion mask. Disable when zone-level wave masking is used instead.")]
    [SerializeField] bool registerWithWaveLinker = false;

    void OnEnable()
    {
        if (registerWithWaveLinker) SoulFishWaveLinker.Register(transform);
        SoulFishMapLinker.Register(transform);
    }

    void OnDisable()
    {
        if (registerWithWaveLinker) SoulFishWaveLinker.Unregister(transform);
        SoulFishMapLinker.Unregister(transform);
    }
}