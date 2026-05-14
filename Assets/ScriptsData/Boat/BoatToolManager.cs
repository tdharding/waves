using UnityEngine;

public enum BoatTool { WhirlSucker, Catapult, Lure }

public class BoatToolManager : MonoBehaviour
{
    [Header("Refs")]
    public GameObject boatCatapultRoot;
    public GameObject boatLureRoot;
    public GameObject crossMastRoot;
    public BoatMovement boatMovement;
    public LureController lureController;

    public BoatTool CurrentTool { get; private set; } = BoatTool.WhirlSucker;

    [Header("Unlocks")]
    public bool whirlUnlocked    = true;
    public bool catapultUnlocked = true;
    public bool lureUnlocked     = true;

    [Header("Lure")]
    public int amountOfLures = 3;

#if UNITY_EDITOR
    public static bool EditorOverrideWhirl     = true;
    public static bool EditorOverrideCatapult  = true;
    public static bool EditorOverrideLure      = true;
#endif

    public bool IsToolUnlocked(BoatTool tool)
    {
        switch (tool)
        {
            case BoatTool.WhirlSucker: return whirlUnlocked;
            case BoatTool.Catapult:    return catapultUnlocked;
            case BoatTool.Lure:        return lureUnlocked && (lureController == null || lureController.HasLureAvailable);
            default: return false;
        }
    }

    private void Start()
    {
#if UNITY_EDITOR
        whirlUnlocked    = EditorOverrideWhirl;
        catapultUnlocked = EditorOverrideCatapult;
        lureUnlocked     = EditorOverrideLure;
#endif
        if (lureController != null)
        {
            lureController.InitStock(amountOfLures);
            lureController.OnLuresExhausted += OnLuresExhausted;
        }

        ApplyTool(CurrentTool);
    }

    public void SetLureController(LureController controller)
    {
        if (lureController != null)
            lureController.OnLuresExhausted -= OnLuresExhausted;

        lureController = controller;
        lureController.InitStock(amountOfLures);
        lureController.OnLuresExhausted += OnLuresExhausted;
        ApplyTool(CurrentTool);
    }

    private void OnDestroy()
    {
        if (lureController != null)
            lureController.OnLuresExhausted -= OnLuresExhausted;
    }

    private void OnLuresExhausted()
    {
        if (CurrentTool == BoatTool.Lure)
            SetTool(BoatTool.WhirlSucker);
        else
            ApplyTool(CurrentTool); // refresh root visibility even if tool didn't change
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
        bool lureActive     = tool == BoatTool.Lure;

        if (boatCatapultRoot != null)
            boatCatapultRoot.SetActive(catapultActive);

        if (crossMastRoot != null)
            crossMastRoot.SetActive(!catapultActive && !lureActive);

        if (boatMovement != null)
            boatMovement.SetCatapultSlow(catapultActive);

        if (boatLureRoot != null)
            boatLureRoot.SetActive(lureActive && (lureController == null || lureController.HasLureAvailable));

        if (lureController != null)
        {
            lureController.enabled = lureActive;
            lureController.SetBoatLureRoot(lureActive ? boatLureRoot : null);
        }
    }
}
