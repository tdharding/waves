using UnityEngine;
using UnityEngine.UI;

public class JunctionPromptUI : MonoBehaviour
{
    public static JunctionPromptUI Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private Image      leftArrow;
    [SerializeField] private Image      rightArrow;

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
        if (leftArrow  != null)
        {
            leftArrow.gameObject.SetActive(hasLeft);
            // Flip horizontally so the right-pointing sprite becomes a left arrow
            var s = leftArrow.transform.localScale;
            leftArrow.transform.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
        }

        if (rightArrow != null)
        {
            rightArrow.gameObject.SetActive(hasRight);
            var s = rightArrow.transform.localScale;
            rightArrow.transform.localScale = new Vector3(Mathf.Abs(s.x), s.y, s.z);
        }

        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
