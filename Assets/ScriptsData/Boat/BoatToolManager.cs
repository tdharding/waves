using UnityEngine;

public enum BoatTool { WhirlSucker, Catapult, Lure }

public class BoatToolManager : MonoBehaviour
{
    [Header("Refs")]
    public GameObject boatCatapultRoot;
    public GameObject crossMastRoot;
    public BoatMovement boatMovement;
    public LureController lureController;

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
        Debug.Log($"[BoatToolManager] Tool selected: {tool}");
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

        if (lureController != null)
            lureController.enabled = (tool == BoatTool.Lure);
    }
}
