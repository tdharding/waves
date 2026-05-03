using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] private string nextSceneName = "LevelSelect";

    [Header("Music")]
    [SerializeField] private PlayThenLoop menuMusic;

    private void Awake()
    {
        menuMusic?.Begin();
    }

    public void OnNewGamePressed()
    {
        GameProgressData.ClearAll();
        Debug.Log("[MainMenuController] New game started — save data cleared.");

        Screen.fullScreen = true;
        SceneManager.LoadScene(nextSceneName);
    }

    public void OnLoadGamePressed()
    {
        Debug.Log("[MainMenuController] Loading existing save data.");

        Screen.fullScreen = true;
        SceneManager.LoadScene(nextSceneName);
    }
}