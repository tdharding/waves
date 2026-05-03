using UnityEngine;
using System.Collections.Generic;

public class SoulFishWaveLinker : MonoBehaviour
{
    [SerializeField] private WaveMaterialController waveController;

    const int MAX_POINTS = 10;

    static readonly Vector3 OffMapPosition = new Vector3(99999f, 99999f, 99999f);

    static readonly int[] PositionIDs =
    {
        Shader.PropertyToID("_SoulFishPosition1"),
        Shader.PropertyToID("_SoulFishPosition2"),
        Shader.PropertyToID("_SoulFishPosition3"),
        Shader.PropertyToID("_SoulFishPosition4"),
        Shader.PropertyToID("_SoulFishPosition5"),
        Shader.PropertyToID("_SoulFishPosition6"),
        Shader.PropertyToID("_SoulFishPosition7"),
        Shader.PropertyToID("_SoulFishPosition8"),
        Shader.PropertyToID("_SoulFishPosition9"),
        Shader.PropertyToID("_SoulFishPosition10"),
    };

    static readonly int CountID = Shader.PropertyToID("_SoulFishCount");

    static readonly List<Transform> activeFish = new List<Transform>();

    public void BakePositionsOnce()
    {
        if (!waveController || !waveController.waveMaterial) return;

        Material mat = waveController.waveMaterial;
        int count = Mathf.Min(activeFish.Count, MAX_POINTS);

        for (int i = 0; i < count; i++)
            mat.SetVector(PositionIDs[i], activeFish[i].position);

        // Push unused slots off-map so they never trigger the mask
        for (int i = count; i < MAX_POINTS; i++)
            mat.SetVector(PositionIDs[i], OffMapPosition);

        mat.SetFloat(CountID, count);

        // Keep map wave renderer in sync
        waveController.SyncMapWaves();
    }

    public static void Register(Transform fish)
    {
        if (activeFish.Contains(fish)) return;
        activeFish.Add(fish);
        FindObjectOfType<SoulFishWaveLinker>()?.BakePositionsOnce();
    }

    public static void Unregister(Transform fish)
    {
        if (!activeFish.Contains(fish)) return;
        activeFish.Remove(fish);
        FindObjectOfType<SoulFishWaveLinker>()?.BakePositionsOnce();
    }

    public static IReadOnlyList<Transform> ActiveFish => activeFish;
}