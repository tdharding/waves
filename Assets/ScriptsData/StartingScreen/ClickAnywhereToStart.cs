using UnityEngine;
using UnityEngine.SceneManagement;

public class ClickAnywhereToStart : MonoBehaviour
{
    [SerializeField] private string nextSceneName = "LevelSelect";
    [SerializeField] private bool clearSaveDataOnStart = false;

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (clearSaveDataOnStart)
            {
                GameProgressData.ClearAll();
                Debug.Log("[ClickAnywhereToStart] Save data cleared.");
            }

            Screen.fullScreen = true;
            SceneManager.LoadScene(nextSceneName);
        }
    }
}