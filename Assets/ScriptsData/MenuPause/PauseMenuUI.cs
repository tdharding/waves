using UnityEngine;

public class PauseMenuUI : MonoBehaviour
{
    public GameObject mainPanel;
    public GameObject aboutPanel;
    public GameObject controlsPanel;
    public GameObject settingsPanel;

    void Start()
    {
        ShowMain();
    }

    public void ShowMain()
    {
        mainPanel.SetActive(true);
        aboutPanel.SetActive(false);
        controlsPanel.SetActive(false);
         settingsPanel.SetActive(false);
    }

    public void ShowAbout()
    {
        mainPanel.SetActive(false);
        aboutPanel.SetActive(true);
        controlsPanel.SetActive(false);
         settingsPanel.SetActive(false);
    }

    public void ShowControls()
    {
        mainPanel.SetActive(false);
        aboutPanel.SetActive(false);
        controlsPanel.SetActive(true);
        settingsPanel.SetActive(false);
    }
      public void ShowSettings()
    {
        mainPanel.SetActive(false);
        aboutPanel.SetActive(false);
        controlsPanel.SetActive(false);
         settingsPanel.SetActive(true);
    }
}
