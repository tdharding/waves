using UnityEngine;
using System.Collections.Generic;

public class SoulFishGlowManager : MonoBehaviour
{
    private static SoulFishGlowManager _instance;
    public static SoulFishGlowManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<SoulFishGlowManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("SoulFishGlowManager");
                    _instance = go.AddComponent<SoulFishGlowManager>();
                }
            }
            return _instance;
        }
    }

    private static readonly int HooverFishPointsID = Shader.PropertyToID("_HooverFishPoints");
    private static readonly int HooverFishCountID = Shader.PropertyToID("_HooverFishCount");

    private readonly List<Transform> trackedFish = new List<Transform>();
    private readonly Vector4[] pointBuffer = new Vector4[8];

    public void RegisterFish(Transform fish)
    {
        if (!trackedFish.Contains(fish)) trackedFish.Add(fish);
    }

    public void UnregisterFish(Transform fish)
    {
        trackedFish.Remove(fish);
    }

    private void LateUpdate()
    {
        int count = 0;
        for (int i = 0; i < trackedFish.Count && count < 8; i++)
        {
            if (trackedFish[i] == null) continue;
            pointBuffer[count++] = trackedFish[i].position;
        }

        Shader.SetGlobalVectorArray(HooverFishPointsID, pointBuffer);
        Shader.SetGlobalFloat(HooverFishCountID, count);
    }
}