using UnityEngine;

public class SoulFishWaveReference : MonoBehaviour
{
    void OnEnable()
    {
        SoulFishWaveLinker.Register(transform);
        SoulFishMapLinker.Register(transform);
    }

    void OnDisable()
    {
        SoulFishWaveLinker.Unregister(transform);
        SoulFishMapLinker.Unregister(transform);
    }
}