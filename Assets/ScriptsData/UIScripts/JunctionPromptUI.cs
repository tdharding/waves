using UnityEngine;
using TMPro;

public class JunctionPromptUI : MonoBehaviour
{
    public static JunctionPromptUI Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text   promptText;

    private void Awake()
    {
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Show(bool hasLeft, bool hasRight)
    {
        string text = "";
        if (hasLeft)  text += "◄ ";
        if (hasRight) text += "► ";
        if (promptText != null) promptText.text = text.Trim();
        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
