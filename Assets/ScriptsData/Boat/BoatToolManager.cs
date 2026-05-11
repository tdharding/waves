using UnityEngine;

public enum BoatTool { WhirlSucker, Catapult }

public class BoatToolManager : MonoBehaviour
{
    [Header("Refs")]
    public GameObject boatCatapultRoot;
    public GameObject crossMastRoot;
    public BoatMovement boatMovement;

    public BoatTool CurrentTool { get; private set; } = BoatTool.WhirlSucker;

    private void Start()
    {
        ApplyTool(CurrentTool);
    }

    public void SetTool(BoatTool tool)
    {
        if (tool == CurrentTool) return;
        CurrentTool = tool;
        ApplyTool(tool);
    }

    private void ApplyTool(BoatTool tool)
    {
        bool catapultActive = tool == BoatTool.Catapult;

        if (boatCatapultRoot != null)
            boatCatapultRoot.SetActive(catapultActive);

        if (crossMastRoot != null)
            crossMastRoot.SetActive(!catapultActive);

        if (boatMovement != null)
            boatMovement.SetCatapultSlow(catapultActive);
    }
}
