using UnityEngine;
using UnityEngine.SceneManagement;

public class SoulWellExit : MonoBehaviour
{
    [Header("Exit Scene")]
    [Tooltip("Name of the scene to load when ESC is pressed")]
    public string sceneToLoad;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ExitSoulWell();
        }
    }

    private void ExitSoulWell()
    {
        if (string.IsNullOrEmpty(sceneToLoad))
        {
            Debug.LogError("SoulWellExit: sceneToLoad is not set.");
            return;
        }

        SceneManager.LoadScene(sceneToLoad);
    }
}
