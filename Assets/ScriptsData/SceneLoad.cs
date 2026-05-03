using UnityEngine;

public class SceneLoad : MonoBehaviour
{
    [Header("Scene Startup Refs")]
    public GameObject mainCamera;


    void Start()
    {
        // 1. Ensure the main camera (with CinemachineBrain) is active
        if (mainCamera != null)
            mainCamera.SetActive(true);


        // 3. Cursor setup
        if (!PauseManager.IsPaused)
        {
  
        }
    }
}
