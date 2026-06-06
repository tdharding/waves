#if UNITY_EDITOR
using UnityEngine;

[ExecuteInEditMode]
public class SoulFishGlowDebugger : MonoBehaviour
{
    public bool showDebug = true;

    private static readonly int HooverFishPointsID = Shader.PropertyToID("_HooverFishPoints");
    private static readonly int HooverFishCountID = Shader.PropertyToID("_HooverFishCount");

    void OnGUI()
    {
        if (!showDebug) return;
        if (!Application.isPlaying && !Application.isEditor) return;

        var controller = Object.FindAnyObjectByType<FishingController>();
        if (controller == null)
        {
            GUI.Label(new Rect(10, 10, 300, 20), "FishingController NOT FOUND");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null) return;

        int count = 0;
        
        GUI.color = Color.yellow;
        GUI.Label(new Rect(10, 30, 300, 20), $"Tracking Fish...");

        foreach (var fish in Object.FindObjectsByType<FishFishingBehaviour>(FindObjectsSortMode.None))
        {
            if (fish.IsTravelingTube)
            {
                Vector3 worldPos = fish.transform.position;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                if (screenPos.z > 0)
                {
                    float size = 40f;
                    GUI.Box(new Rect(screenPos.x - size / 2, Screen.height - screenPos.y - size / 2, size, size), "");
                    GUI.Label(new Rect(screenPos.x + size, Screen.height - screenPos.y, 200, 20), $"Fish {count} (Traveling)");
                }
                count++;
            }
        }

        if (count == 0)
        {
            GUI.Label(new Rect(10, 50, 300, 20), "No fish currently 'TravelingTube'");
        }
    }
}
#else
using UnityEngine;
public class SoulFishGlowDebugger : MonoBehaviour {}
#endif
