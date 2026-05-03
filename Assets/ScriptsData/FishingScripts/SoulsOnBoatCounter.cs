using UnityEngine;
using TMPro;

public class SoulsOnBoatCounter : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI soulText;

    private void Start()
    {
        UpdateText();
    }

    private void Update()
    {
        UpdateText();
    }

    private void UpdateText()
    {
        int souls = LevelSoulTracker.Instance != null
            ? LevelSoulTracker.Instance.SoulsOnBoat
            : GameProgressData.GetSoulsOnBoat();

        soulText.text = "Souls on Boat " + souls;
    }
}